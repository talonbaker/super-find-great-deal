using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>Placement integrity, layer 2</b> — the rest-time audit (program doc §5b, Talon 2026-09-19).
/// Layer 1 (CARRY-1) says whether a prop may be PUT somewhere; this says whether it may STAY
/// where physics left it, and puts it back when it may not.
///
/// <para><b>Why a layer 1 that already passed is not enough.</b> A placement is checked at the
/// transform the player asked for. What actually happens next is a solver: the crate rolls, a
/// second crate lands on it, a shelf is knocked, and the pose it comes to rest in is nobody's
/// decision. Every prior playtest in this lineage has props clipping into walls and each other,
/// and in THIS game a seeker who cannot find the object because of a physics glitch is the worst
/// outcome the design has. So the same two tests run again on the transform the prop actually
/// settled at, and a failure is CORRECTED rather than reported.</para>
///
/// <para><b>The failure path, in order, and the order is the design:</b>
/// <list type="number">
/// <item><b>Depenetrate</b> along the minimum translation, up to
/// <see cref="PropManager.DepenetrateMaxM"/>, and re-test. This is the common case — a crate
/// 4 cm into a wall — and it keeps the prop where the player left it, which matters because the
/// hider chose that spot.</item>
/// <item><b>Otherwise return it to its last good transform</b> — the last place or rest pose that
/// passed. <b>Never to spawn.</b> Spawn is a rescue that undoes the player's hiding; last-good is
/// a rescue that undoes the physics.</item>
/// </list></para>
///
/// <para><b>Every correction logs one line naming the prop, the reason and BOTH transforms.</b>
/// A prop that teleports with no audit behind it is indistinguishable from a desync, and the
/// line is how a playtest proves which it was.</para>
///
/// <para><b>Cost: one shape query per settle event</b> on the passing path, which is what §5b
/// costed it at. A failure pays for the correction, and failures are rare by construction.</para>
/// </summary>
public static class RestAudit
{
    /// <summary>Why the audit acted. These four names are the packet's, verbatim, because they
    /// are what a handoff and a playtest log get grepped for.</summary>
    public enum Reason
    {
        /// <summary>Nothing was wrong.</summary>
        None = 0,

        /// <summary>The prop came to rest inside static geometry — a wall, a shelf back, a
        /// pillar. <b>This is the one the design must never ship.</b></summary>
        StaticOverlap = 1,

        /// <summary>The prop came to rest inside another prop. Usually benign (the solver is
        /// about to separate them) but audited identically, because "usually" is not a rule.
        /// </summary>
        PropOverlap = 2,

        /// <summary>The prop came to rest outside every authored room bounds volume.</summary>
        OutOfBounds = 3,

        /// <summary>The prop fell through the world and crossed the shared kill plane.</summary>
        KillPlane = 4,
    }

    /// <summary>What the audit did about it.</summary>
    public enum Outcome
    {
        /// <summary>The rest transform passed; it is now this prop's last good transform.</summary>
        Good = 0,

        /// <summary>Pushed out along the minimum translation; the corrected pose passes and is
        /// now the last good transform.</summary>
        Depenetrated = 1,

        /// <summary>Could not be corrected in place; returned to the last good transform.</summary>
        RestoredLastGood = 2,

        /// <summary>Could not be corrected AND the last good transform does not pass either — a
        /// prop that has never had a legal pose (authored inside the scenery, or spawned there by
        /// a dev script). Left at the last good transform anyway, because it is still the best
        /// answer available, and reported LOUDLY: this is the case layer 3 then refuses Confirm
        /// on.</summary>
        Stuck = 3,
    }

    /// <summary>One audit's whole answer.</summary>
    /// <param name="Reason">Which test failed, or <see cref="Reason.None"/>.</param>
    /// <param name="Outcome">What was done about it.</param>
    /// <param name="From">The transform the prop was at when the audit ran.</param>
    /// <param name="To">The transform it is at afterwards. Equal to <paramref name="From"/> on
    /// the good path.</param>
    /// <param name="PenetrationM">Overlap depth that triggered it, 0 for the other reasons.</param>
    /// <param name="Detail">What it hit, from <see cref="PlacementIntegrity.Verdict.Detail"/>.</param>
    /// <param name="Queries">Physics queries this audit issued — the cost instrumentation the
    /// packet asks for, counted rather than estimated.</param>
    public readonly record struct Result(Reason Reason, Outcome Outcome, Transform3D From,
        Transform3D To, float PenetrationM, string Detail, int Queries)
    {
        /// <summary>True when the audit moved the prop.</summary>
        public bool Corrected => Outcome is Outcome.Depenetrated or Outcome.RestoredLastGood
            or Outcome.Stuck;

        /// <summary>True when the prop is, as far as this layer can tell, inside static geometry
        /// and could not be got out. <see cref="Reachability.IReachSampler.TargetInsideStatic"/>
        /// is fed from this.</summary>
        public bool StuckInStatic => Outcome == Outcome.Stuck && Reason is Reason.StaticOverlap;

        /// <summary>The one log line. Names the prop's two transforms because "it moved" without
        /// "from where to where" is not something a playtest can check against a capture.</summary>
        public string Describe(int propId) =>
            $"prop={propId} {Reason} -> {Outcome} "
            + $"from=({From.Origin.X:0.00}, {From.Origin.Y:0.00}, {From.Origin.Z:0.00}) "
            + $"to=({To.Origin.X:0.00}, {To.Origin.Y:0.00}, {To.Origin.Z:0.00}) "
            + (PenetrationM > 0f ? $"depth={PenetrationM:0.000}m " : "")
            + Detail;
    }

    /// <summary>
    /// Audit <paramref name="prop"/> at the transform it just came to rest at.
    /// </summary>
    /// <param name="prop">The prop. Its <see cref="NetworkedProp.LastGoodTransform"/> is read and,
    /// on the good path, written.</param>
    /// <param name="restAt">Where physics left it.</param>
    /// <param name="toleranceM">Overlap tolerance — <see cref="PropManager.PlaceOverlapToleranceM"/>
    /// on every real call site; a parameter so layer 1 and layer 2 can never drift to two
    /// different numbers by accident.</param>
    /// <param name="maxDepenetrateM">How far a correction may push, metres.</param>
    public static Result Audit(NetworkedProp prop, Transform3D restAt, float toleranceM,
        float maxDepenetrateM)
    {
        int queries = 1;
        PlacementIntegrity.Verdict verdict =
            PlacementIntegrity.Check(prop.Body, restAt, null, toleranceM);
        if (verdict.Allowed)
        {
            prop.NoteLastGood(restAt);
            return new Result(Reason.None, Outcome.Good, restAt, restAt, 0f, "", queries);
        }

        Reason why = ReasonFor(verdict);
        return Correct(prop, restAt, why, verdict, toleranceM, maxDepenetrateM, queries);
    }

    /// <summary>
    /// The kill-plane path. Same correction policy, a different trigger: the prop is not
    /// overlapping anything, it is simply gone — below the shared out-of-bounds floor.
    ///
    /// <para><b>To the last good transform, not to spawn</b> (§5b, and the packet says it in as
    /// many words). The pre-REACH-1 behaviour was <c>HomeTransform</c>, which for an authored prop
    /// is where the LEVEL put it — so a hider who hid the target in the far aisle and then saw a
    /// seeker knock it off a ledge would have watched the game hand it back to its starting
    /// shelf. For a prop that has never rested anywhere legal the two are the same value
    /// (<see cref="NetworkedProp.Init"/> seeds last-good from the spawn pose), which is why this
    /// change moves nothing in the existing throw/OOB suites.</para>
    /// </summary>
    public static Result AuditKillPlane(NetworkedProp prop, Transform3D fellFrom, float toleranceM)
    {
        Transform3D home = prop.LastGoodTransform;
        int queries = 1;
        PlacementIntegrity.Verdict at = PlacementIntegrity.Check(prop.Body, home, null, toleranceM);
        Outcome outcome = at.Allowed ? Outcome.RestoredLastGood : Outcome.Stuck;
        return new Result(Reason.KillPlane, outcome, fellFrom, home, 0f,
            at.Allowed ? "returned to last good" : $"last good is itself bad: {at}", queries);
    }

    private static Result Correct(NetworkedProp prop, Transform3D restAt, Reason why,
        PlacementIntegrity.Verdict verdict, float toleranceM, float maxDepenetrateM, int queries)
    {
        // 1. Push it out, if a push of the allowed size can do it.
        // toleranceM, not the default: layer 1 and layer 2 must judge by one number, and this is
        // the call site Check's own parameter doc names (REVIEW-1 I5).
        if (PlacementIntegrity.TryDepenetrate(prop.Body, restAt, maxDepenetrateM, toleranceM,
                out Transform3D pushed, out float movedM, out int pushQueries))
        {
            queries += pushQueries;
            prop.NoteLastGood(pushed);
            return new Result(why, Outcome.Depenetrated, restAt, pushed, verdict.PenetrationM,
                $"pushed {movedM:0.000} m; {verdict.Detail}", queries);
        }
        queries += pushQueries;

        // 2. Otherwise home to the last good pose — and check that one too, because a prop that
        //    was born inside the scenery has a last-good that is itself illegal, and telling the
        //    log "restored" about that would be a lie the round then trips over at Confirm.
        Transform3D lastGood = prop.LastGoodTransform;
        queries++;
        bool lastGoodOk = PlacementIntegrity.Check(prop.Body, lastGood, null, toleranceM).Allowed;
        return new Result(why, lastGoodOk ? Outcome.RestoredLastGood : Outcome.Stuck,
            restAt, lastGood, verdict.PenetrationM,
            lastGoodOk ? verdict.Detail : $"last good is itself bad; {verdict.Detail}", queries);
    }

    private static Reason ReasonFor(PlacementIntegrity.Verdict v) => v.Fault switch
    {
        PlacementIntegrity.PlacementFault.OutsideRoomBounds => Reason.OutOfBounds,
        PlacementIntegrity.PlacementFault.Overlapping =>
            v.BlockerIsProp ? Reason.PropOverlap : Reason.StaticOverlap,
        // A prop with no collision shape is a level defect. It cannot be inside anything and
        // cannot be depenetrated; calling it a static overlap is the honest bucket, because the
        // correction path (last good, then loud) is exactly the one it needs.
        _ => Reason.StaticOverlap,
    };
}
