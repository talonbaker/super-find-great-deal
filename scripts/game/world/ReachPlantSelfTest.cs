using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Round;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The planted-room self-test</b> — §5b's six cases, each authored as a real fixture in
/// <c>ReachPlantRoom.tscn</c>, each audited by the real layer-2 and layer-3 code on a real
/// server with a real physics space. Armed by <c>--reach-selftest</c>;
/// <c>tests/Run-ReachTest.ps1</c> gates on the LINES it prints, not on the exit code (the reason
/// <see cref="SupermarketWorldSelfTest"/> does: a process that dies before its own summary exits
/// non-zero for reasons that have nothing to do with the subject).
///
/// <para><b>What is real here and what is staged, stated plainly.</b> The AUDIT is the shipped
/// one: every case calls <see cref="PropManager.ServerAuditRest"/>, which is the identical method
/// the live settle latch calls, and reachability is the shipped
/// <see cref="Reachability.Evaluate"/> over the shipped <see cref="PhysicsReachSampler"/>. What
/// is staged is the TRIGGER: an authored prop is frozen at rest and has therefore never latched,
/// so there is no settle event to wait for. Re-implementing the latch in the test to produce one
/// would be testing the test. The one case that goes end to end through the live loop is the
/// faller, which is nudged into real loose physics and recovered by
/// <c>PropManager._PhysicsProcess</c>'s own kill-plane branch with nothing here involved.</para>
///
/// <para><b>Seven cases, not six.</b> The extra one is the shallow overlap that depenetration
/// CLEARS. Without it the scene proves the fallback branch three times and the correction branch
/// never — and the correction is the branch that runs in a real session, where a crate 4 cm into
/// a shelf is common and a crate buried in a wall is not.</para>
///
/// <para><b>Eight, since REVIEW-1 (2026-09-20).</b> The last one is the Confirm-time audit
/// against a target that is still in the air — the one case where the audit could MOVE the game
/// rather than measure it. It goes through the shipped
/// <see cref="ReachabilityFactSource.AuditBeforeConfirm"/>, not through
/// <see cref="PropManager.ServerAuditRest"/> directly, because the defect was in which of those
/// two the press reaches.</para>
/// </summary>
public sealed partial class ReachPlantSelfTest : Node
{
    /// <summary>Prefix on every line, so a runner can grep this test's output out of the
    /// engine's and the server's.</summary>
    public const string Prefix = "[reach-selftest]";

    /// <summary>The one machine-readable line the runner gates on.</summary>
    public const string SummaryPrefix = Prefix + " SUMMARY";

    /// <summary>Physics frames to let pass before auditing anything. The props are frozen, so
    /// nothing is settling — this is for the adoption pass and the spawner to have run, which is
    /// a frame-order fact rather than a physics one.</summary>
    private const int WarmupFrames = 30;

    /// <summary>How long the faller gets to fall past the kill plane and be recovered, seconds.
    /// A ceiling, not an expectation: the assertion is on where it ends up, and the run reports
    /// how long it actually took.</summary>
    private const float FallerTimeoutSec = 10f;

    /// <summary>How close the recovered faller must be to its last good transform, metres. A
    /// teleport is exact; this is slack for one physics frame of settling on the floor
    /// underneath it.</summary>
    private const float RecoveryToleranceM = 0.05f;

    /// <summary>The prop manager. Set by <c>Gameplay</c> before this node is added.</summary>
    public PropManager Props { get; set; } = null!;

    private enum Stage { Warmup, Statics, Faller, ConfirmOnAMover, DepenetrateTolerance, Done }

    /// <summary>REVIEW-1 I5. The tolerance the depenetration case hands
    /// <see cref="PlacementIntegrity.TryDepenetrate"/>, metres. Chosen ABOVE the plinth overlap
    /// <see cref="StaticCases"/> measures at 0.125 m and far above
    /// <see cref="PlacementIntegrity.DefaultOverlapToleranceM"/> (0.02 m), so the two answers are
    /// unmistakably different: a loop that honours its caller finds nothing to push and moves
    /// 0.000 m, and a loop that re-tests against the hard-coded default pushes the whole
    /// 0.125 m.</summary>
    private const float ToleranceCaseM = 0.20f;

    /// <summary>How far the prop may be moved by a <see cref="ToleranceCaseM"/>-tolerance
    /// depenetration before the loop is judging by somebody else's bar, metres.</summary>
    private const float ToleranceCaseMaxMoveM = 0.001f;

    /// <summary>The prop the Confirm-time case shoves. It is 3 m up on a ledge, so an upward
    /// nudge buys a long, unambiguous flight and the assertion never has to race a settle.
    /// Audited already by <see cref="StaticCases"/>, which is what makes its mode Resting going
    /// in — the precondition the case is about.</summary>
    private const string MoverNode = "Prop_2_HighLedge";

    /// <summary>Physics frames between the shove and the Confirm press. Two, so the body is
    /// genuinely in motion rather than merely un-frozen when the audit runs.</summary>
    private const int MoverSettleFrames = 2;

    /// <summary>Physics frames after the press before the "it is still falling" check. At 60 Hz
    /// and ~4 m/s a tenth of a second is tens of centimetres, far outside
    /// <see cref="MoverStillMovingM"/>.</summary>
    private const int MoverWatchFrames = 6;

    /// <summary>How far the shoved prop must have travelled in <see cref="MoverWatchFrames"/>
    /// after the press for the press to have left physics alone, metres. A frozen body moves
    /// exactly 0.000.</summary>
    private const float MoverStillMovingM = 0.05f;

    /// <summary>One planted case: the node to look up, what layer 2 must say, and what layer 3
    /// must say. Written as a table rather than as seven methods because the table IS the
    /// packet's list and a reader should be able to check one against the other.</summary>
    private sealed record Case(
        string NodeName,
        string What,
        RestAudit.Reason ExpectReason,
        RestAudit.Outcome ExpectOutcome,
        bool ExpectReachable,
        Reachability.ReachReason ExpectWhy,
        // What the winning ray must have met first. Asserted only on the reachable cases, and it
        // is the assertion that gives the bin and the box their teeth: without it, a room where
        // the bin was never authored would pass both of them on a clear line to the crate.
        Reachability.ReachHit ExpectWinner = Reachability.ReachHit.Static);

    private static readonly Case[] StaticCases =
    {
        new("Prop_0_Wall", "inside a wall",
            RestAudit.Reason.StaticOverlap, RestAudit.Outcome.Stuck,
            false, Reachability.ReachReason.InsideStatic),
        new("Prop_1_ShelfBack", "inside a static shelf back",
            RestAudit.Reason.StaticOverlap, RestAudit.Outcome.Stuck,
            false, Reachability.ReachReason.InsideStatic),
        new("Prop_2_HighLedge", "3 m up",
            RestAudit.Reason.None, RestAudit.Outcome.Good,
            false, Reachability.ReachReason.NoStandingPoint),
        new("Prop_3_UnderBin", "under an overturned bin",
            RestAudit.Reason.None, RestAudit.Outcome.Good,
            true, Reachability.ReachReason.Reachable, Reachability.ReachHit.MovableProp),
        new("Prop_5_InBox", "inside a closed movable box",
            RestAudit.Reason.None, RestAudit.Outcome.Good,
            true, Reachability.ReachReason.Reachable, Reachability.ReachHit.MovableProp),
        new("Prop_8_Shallow", "0.12 m into a plinth (depenetrates)",
            RestAudit.Reason.StaticOverlap, RestAudit.Outcome.Depenetrated,
            true, Reachability.ReachReason.Reachable, Reachability.ReachHit.Target),
    };

    private const string FallerNode = "Prop_7_Faller";

    private readonly List<string> _failures = new();
    private readonly List<string> _verdicts = new();
    private Stage _stage = Stage.Warmup;
    private int _frames;
    private float _fallerElapsed;
    private bool _fallerNudged;
    private bool _fallerSawVoid;
    private Transform3D _fallerLastGood;
    private int _fallerPropId = -1;
    private int _totalQueries;

    private int _moverFrames;
    private int _moverPropId = -1;
    private bool _moverShoved;
    private bool _moverPressed;
    private Vector3 _moverAtPress;
    private PropMode? _moverModeAtPress;
    private bool _moverFrozenAtPress;

    public override void _Ready()
    {
        GD.Print($"{Prefix} planted room: §5b's six cases plus the depenetration branch");
        GD.Print($"{Prefix} grab reach {Reachability.GrabReachM:0.000} m "
                 + $"({Reachability.RingSamples} points per ring, two rings), "
                 + $"floor probe {Reachability.FloorProbeM:0.00} m, "
                 + $"depenetrate ceiling {PropManager.DepenetrateMaxM:0.00} m, "
                 + $"overlap tolerance {PropManager.PlaceOverlapToleranceM:0.000} m");
    }

    public override void _PhysicsProcess(double delta)
    {
        switch (_stage)
        {
            case Stage.Warmup:
                if (++_frames >= WarmupFrames)
                {
                    RunStatics();
                    _stage = Stage.Faller;
                }
                break;

            case Stage.Faller:
                TickFaller((float)delta);
                break;

            case Stage.ConfirmOnAMover:
                TickConfirmOnAMover();
                break;

            case Stage.DepenetrateTolerance:
                RunDepenetrateTolerance();
                Finish();
                break;
        }
    }

    // --- the six that are audited where they sit ----------------------------------------------

    private void RunStatics()
    {
        foreach (Case c in StaticCases)
        {
            NetworkedProp? prop = FindProp(c.NodeName);
            if (prop is null)
            {
                Fail($"{c.NodeName} is not in the planted room at all — the fixture is missing, "
                     + "which is a scene defect and not a verdict about the audit");
                _verdicts.Add($"{Prefix} VERDICT {c.NodeName} ({c.What}) MISSING -> FAIL");
                continue;
            }

            Transform3D before = prop.Body.GlobalTransform;
            RestAudit.Result audit = Props.ServerAuditRest(prop.PropId);
            Reachability.ReachVerdict reach = EvaluateReach(prop, out int queries);
            _totalQueries += audit.Queries + queries;

            var problems = new List<string>();
            if (audit.Reason != c.ExpectReason)
                problems.Add($"layer2 reason {audit.Reason}, expected {c.ExpectReason}");
            if (audit.Outcome != c.ExpectOutcome)
                problems.Add($"layer2 outcome {audit.Outcome}, expected {c.ExpectOutcome}");
            if (reach.Reachable != c.ExpectReachable)
                problems.Add($"layer3 said {(reach.Reachable ? "reachable" : "unreachable")}, "
                             + $"expected {(c.ExpectReachable ? "reachable" : "unreachable")}");
            if (!c.ExpectReachable && reach.Reason != c.ExpectWhy)
                problems.Add($"layer3 reason {reach.Reason}, expected {c.ExpectWhy}");
            if (c.ExpectReachable && reach.Winner != c.ExpectWinner)
                problems.Add($"layer3's winning ray met {reach.Winner} first, "
                             + $"expected {c.ExpectWinner}");

            float moved = before.Origin.DistanceTo(prop.Body.GlobalTransform.Origin);
            string verdict = problems.Count == 0 ? "PASS" : "FAIL";
            _verdicts.Add($"{Prefix} VERDICT {c.NodeName} ({c.What}) prop={prop.PropId} "
                          + $"layer2={audit.Reason}/{audit.Outcome} moved={moved:0.000}m "
                          + $"layer3={(reach.Reachable ? "reachable" : "UNREACHABLE")}/{reach.Reason} "
                          + $"pts={reach.PointsCounting}/{reach.PointsStandable}of{reach.PointsSampled} "
                          + (reach.Reachable ? $"firstHit={reach.Winner} " : "")
                          + $"-> {verdict}");
            foreach (string p in problems)
                Fail($"{c.NodeName} ({c.What}): {p}");
        }
    }

    // --- the one that goes through the live loop ----------------------------------------------

    private void TickFaller(float delta)
    {
        NetworkedProp? faller = FindProp(FallerNode);
        if (faller is null)
        {
            Fail($"{FallerNode} is not in the planted room — the pit case cannot run");
            _verdicts.Add($"{Prefix} VERDICT {FallerNode} (pushed through the floor) MISSING -> FAIL");
            _stage = Stage.ConfirmOnAMover;
            return;
        }

        if (!_fallerNudged)
        {
            // Audit it where it is FIRST, so its last good transform is the authored pose and
            // the recovery has something honest to aim at. (It is also the pose the assertion
            // below compares against, read from the prop rather than typed here — a coordinate
            // typed in two files is a coordinate that disagrees with itself eventually.)
            _fallerPropId = faller.PropId;
            RestAudit.Result pre = Props.ServerAuditRest(_fallerPropId);
            _totalQueries += pre.Queries;
            _fallerLastGood = faller.LastGoodTransform;
            if (pre.Outcome != RestAudit.Outcome.Good)
                Fail($"{FallerNode} did not start from a good pose: {pre.Reason}/{pre.Outcome} — "
                     + "the recovery target would then be meaningless");
            Props.ServerNudgeLoose(_fallerPropId, new Vector3(0f, -4f, 0f));
            GD.Print($"{Prefix} {FallerNode} (prop {_fallerPropId}) shoved into the pit from "
                     + $"({_fallerLastGood.Origin.X:0.00}, {_fallerLastGood.Origin.Y:0.00}, "
                     + $"{_fallerLastGood.Origin.Z:0.00}); kill plane is y={MpFoundation.Net.NetProfile.KillPlaneY:0.0}");
            _fallerNudged = true;
            return;
        }

        _fallerElapsed += delta;
        if (faller.Body.GlobalPosition.Y < MpFoundation.Net.NetProfile.KillPlaneY * 0.5f)
            _fallerSawVoid = true;

        float back = faller.Body.GlobalPosition.DistanceTo(_fallerLastGood.Origin);
        bool recovered = _fallerSawVoid && back <= RecoveryToleranceM;
        if (!recovered && _fallerElapsed < FallerTimeoutSec)
            return;

        var problems = new List<string>();
        if (!_fallerSawVoid)
            problems.Add($"never fell past half the kill plane in {_fallerElapsed:0.0} s "
                         + "— this run never exercised the recovery at all");
        if (back > RecoveryToleranceM)
            problems.Add($"ended {back:0.000} m from its last good transform "
                         + $"(tolerance {RecoveryToleranceM:0.000} m)");

        RestAudit.Result? last = Props.LastAuditFor(_fallerPropId);
        if (last is not { Reason: RestAudit.Reason.KillPlane })
            problems.Add($"its newest audit is {(last is null ? "none" : last.Value.Reason.ToString())}, "
                         + "expected KillPlane — something other than the kill-plane path moved it");

        string verdict = problems.Count == 0 ? "PASS" : "FAIL";
        _verdicts.Add($"{Prefix} VERDICT {FallerNode} (pushed through the floor) prop={_fallerPropId} "
                      + $"layer2={(last is null ? "none" : $"{last.Value.Reason}/{last.Value.Outcome}")} "
                      + $"fell={_fallerSawVoid} back={back:0.003}m in {_fallerElapsed:0.00}s -> {verdict}");
        foreach (string p in problems)
            Fail($"{FallerNode} (pushed through the floor): {p}");

        _stage = Stage.ConfirmOnAMover;
    }

    // --- the one the Confirm press can MOVE rather than measure --------------------------------

    /// <summary>
    /// <b>REVIEW-1 C2: a Confirm press must not force-settle a target that is still in motion.</b>
    ///
    /// <para><c>ReachabilityFactSource.AuditBeforeConfirm</c> used to call
    /// <see cref="PropManager.ServerAuditRest"/> unconditionally, and that method guards only
    /// <see cref="PropMode.Held"/> — it cannot guard on Resting, because the settle latch calls
    /// it on a prop that is still Loose. So a LOOSE target reached
    /// <c>NetworkedProp.SettleToRest</c>, which sets <c>Body.Freeze = true</c>, zeroes both
    /// velocities and pins the transform; a prop a metre up passes layer 2's bounds and overlap
    /// tests, so the audit returned Good and the object was latched in mid-air. The hider drops
    /// or throws the target at the Confirm plate, presses inside the ~0.3 s settle window, and
    /// the seeker is sent to find a can hanging off the floor.</para>
    ///
    /// <para><b>It goes through the shipped press path</b>, not through <c>ServerAuditRest</c>:
    /// the defect was in WHICH of those two a press reaches, so a case that called the audit
    /// directly would prove nothing about the fix. Three assertions, and the third is the one
    /// that cannot be satisfied by accident — the prop must still be TRAVELLING afterwards.</para>
    /// </summary>
    private void TickConfirmOnAMover()
    {
        NetworkedProp? mover = FindProp(MoverNode);
        if (mover is null)
        {
            Fail($"{MoverNode} is not in the planted room — the Confirm-on-a-mover case cannot run");
            _verdicts.Add($"{Prefix} VERDICT {MoverNode} (Confirm while it is still falling) MISSING -> FAIL");
            Finish();
            return;
        }

        if (!_moverShoved)
        {
            _moverPropId = mover.PropId;
            PropMode? before = Props.ModeOf(_moverPropId);
            if (before != PropMode.Resting)
                Fail($"{MoverNode} was {(before?.ToString() ?? "unknown")} before the shove, not "
                     + "Resting — the case's own precondition is broken, so whatever it measures "
                     + "afterwards is about a different world");
            Props.ServerNudgeLoose(_moverPropId, new Vector3(0f, 4.5f, 0f));
            GD.Print($"{Prefix} {MoverNode} (prop {_moverPropId}) shoved upward; the Confirm press "
                     + $"lands in {MoverSettleFrames} frame(s), while it is still in the air");
            _moverShoved = true;
            _moverFrames = 0;
            return;
        }

        _moverFrames++;

        if (!_moverPressed)
        {
            if (_moverFrames < MoverSettleFrames)
                return;

            // The shipped Confirm-time seam, wired exactly as Gameplay wires it: the source
            // holds the prop manager and reads the phase through a closure.
            var reach = new ReachabilityFactSource(Props, () => HideSeekPhase.Hiding)
            {
                TargetPropId = _moverPropId,
            };
            reach.AuditBeforeConfirm();

            _moverModeAtPress = Props.ModeOf(_moverPropId);
            _moverFrozenAtPress = mover.Body.Freeze;
            _moverAtPress = mover.Body.GlobalPosition;
            _moverPressed = true;
            _moverFrames = 0;
            return;
        }

        if (_moverFrames < MoverWatchFrames)
            return;

        float travelled = mover.Body.GlobalPosition.DistanceTo(_moverAtPress);
        var problems = new List<string>();
        if (_moverModeAtPress != PropMode.Loose)
            problems.Add($"the registry said {_moverModeAtPress?.ToString() ?? "unknown"} straight "
                         + "after the press, expected Loose — the press latched a moving prop to rest");
        if (_moverFrozenAtPress)
            problems.Add("Body.Freeze was true straight after the press — SettleToRest ran on a "
                         + "prop that was still in the air");
        if (travelled <= MoverStillMovingM)
            problems.Add($"it travelled {travelled:0.000} m in {MoverWatchFrames} frames after the "
                         + $"press (bar {MoverStillMovingM:0.000} m) — physics stopped owning it");

        string verdict = problems.Count == 0 ? "PASS" : "FAIL";
        _verdicts.Add($"{Prefix} VERDICT {MoverNode} (Confirm while it is still falling) "
                      + $"prop={_moverPropId} modeAfterPress={_moverModeAtPress?.ToString() ?? "unknown"} "
                      + $"frozenAfterPress={_moverFrozenAtPress} "
                      + $"travelledAfter={travelled:0.000}m -> {verdict}");
        foreach (string p in problems)
            Fail($"{MoverNode} (Confirm while it is still falling): {p}");

        _stage = Stage.DepenetrateTolerance;
    }

    // --- the one where layer 1 and layer 2 can drift apart ------------------------------------

    /// <summary>
    /// <b>REVIEW-1 I5: <see cref="PlacementIntegrity.TryDepenetrate"/> must judge its corrections
    /// by the tolerance it was GIVEN.</b>
    ///
    /// <para><c>Check</c>'s own parameter doc says <c>overlapToleranceM</c> exists "so layer 1 and
    /// layer 2 can never drift to two different numbers by accident", and
    /// <c>RestAudit.Correct</c> duly hands the caller's value down. The depenetration loop then
    /// threw it away: it compared the measured depth against
    /// <see cref="PlacementIntegrity.DefaultOverlapToleranceM"/> and re-tested each pushed
    /// candidate with a bare <c>Check(propBody, candidate)</c>. Today the two numbers are aliased
    /// (<c>PropManager.PlaceOverlapToleranceM</c> IS the default), so nothing is visibly wrong —
    /// which is exactly what makes it a trap: the day anyone moves one, the loop starts reporting
    /// <c>Depenetrated</c> for a pose the audit that called it then refuses, or refuses a pose the
    /// audit would have accepted and sends a hidden object back to its last-good transform for no
    /// reason.</para>
    ///
    /// <para><b>The case makes the two numbers differ on purpose</b>, which is the only way to see
    /// it at all: <c>Prop_8_Shallow</c> sits 0.12 m into a plinth, and at a tolerance of
    /// <see cref="ToleranceCaseM"/> there is nothing to correct. A loop that honours its caller
    /// moves it 0.000 m; the shipped-default loop pushes it the full 0.125 m the case above
    /// measures.</para>
    /// </summary>
    private void RunDepenetrateTolerance()
    {
        NetworkedProp? shallow = FindProp("Prop_8_Shallow");
        if (shallow is null)
        {
            Fail("Prop_8_Shallow is not in the planted room — the depenetration-tolerance case "
                 + "cannot run");
            _verdicts.Add($"{Prefix} VERDICT Prop_8_Shallow (depenetration honours the caller's "
                          + "tolerance) MISSING -> FAIL");
            return;
        }

        // Where it sits NOW: the statics pass already depenetrated it, so re-running at the
        // default tolerance from here finds nothing. The overlap this case needs is the authored
        // one, which RestAudit recorded as the audit's `From` pose.
        Transform3D authored = Props.LastAuditFor(shallow.PropId) is { } a
            ? a.From
            : shallow.Body.GlobalTransform;

        bool generous = PlacementIntegrity.TryDepenetrate(shallow.Body, authored,
            PropManager.DepenetrateMaxM, ToleranceCaseM,
            out Transform3D _, out float generousMoved, out int generousQueries);
        bool strict = PlacementIntegrity.TryDepenetrate(shallow.Body, authored,
            PropManager.DepenetrateMaxM, PlacementIntegrity.DefaultOverlapToleranceM,
            out Transform3D _, out float strictMoved, out int strictQueries);
        _totalQueries += generousQueries + strictQueries;

        var problems = new List<string>();
        if (generousMoved > ToleranceCaseMaxMoveM)
            problems.Add($"a {ToleranceCaseM:0.000} m tolerance still pushed it "
                         + $"{generousMoved:0.000} m — the loop is judging by "
                         + $"DefaultOverlapToleranceM ({PlacementIntegrity.DefaultOverlapToleranceM:0.000} m) "
                         + "rather than by the value it was handed");
        // The positive control, and it is not optional: a TryDepenetrate that had simply stopped
        // correcting anything would satisfy the assertion above perfectly.
        if (!strict || strictMoved <= ToleranceCaseMaxMoveM)
            problems.Add($"at the shipped {PlacementIntegrity.DefaultOverlapToleranceM:0.000} m "
                         + $"tolerance it returned {strict} after {strictMoved:0.000} m — the "
                         + "control says this fixture no longer depenetrates at all, so the case "
                         + "above proves nothing");

        string verdict = problems.Count == 0 ? "PASS" : "FAIL";
        _verdicts.Add($"{Prefix} VERDICT Prop_8_Shallow (depenetration honours the caller's "
                      + $"tolerance) prop={shallow.PropId} "
                      + $"at{ToleranceCaseM:0.00}m={generous}/{generousMoved:0.000}m "
                      + $"at{PlacementIntegrity.DefaultOverlapToleranceM:0.00}m={strict}/{strictMoved:0.000}m "
                      + $"-> {verdict}");
        foreach (string p in problems)
            Fail($"Prop_8_Shallow (depenetration honours the caller's tolerance): {p}");
    }

    // --- plumbing -----------------------------------------------------------------------------

    private Reachability.ReachVerdict EvaluateReach(NetworkedProp prop, out int queries)
    {
        bool insideStatic = Props.LastAuditFor(prop.PropId) is { } a && a.StuckInStatic;
        var sampler = new PhysicsReachSampler(prop, Props.LiveProps, insideStatic);
        Reachability.ReachVerdict verdict = Reachability.Evaluate(sampler);
        queries = sampler.Queries;
        return verdict;
    }

    private NetworkedProp? FindProp(string nodeName)
    {
        foreach (NetworkedProp prop in Props.LiveProps)
            if (prop.Name.ToString() == nodeName)
                return prop;
        return null;
    }

    private void Fail(string message)
    {
        _failures.Add(message);
        GD.PrintErr($"{Prefix} FAIL: {message}");
    }

    private void Finish()
    {
        _stage = Stage.Done;
        foreach (string v in _verdicts)
            GD.Print(v);

        GD.Print($"{Prefix} audits={Props.RestAuditCount} corrections={Props.RestCorrectionCount} "
                 + $"stuck={Props.StuckPropCount} integrity-queries={Props.IntegrityQueryCount} "
                 + $"reach-queries={_totalQueries - Props.IntegrityQueryCount}");
        // +3: the faller, and REVIEW-1's Confirm-on-a-mover and depenetration-tolerance cases.
        GD.Print($"{SummaryPrefix} cases={StaticCases.Length + 3} failures={_failures.Count} "
                 + $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
