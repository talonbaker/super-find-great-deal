using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>REACH-1 owns <see cref="IRoundFactSource.TargetRetrievable"/>.</b> Program doc §5b layer 3:
/// before Hiding → Seeking commits, a seeker must be able, in principle, to walk up to the
/// hidden object and grab it or dig it out from under movable things. This source is where that
/// fact enters the round; <c>HideSeekLoop</c> refuses on it with
/// <see cref="HideSeekRefusal.NobodyCouldReachThat"/> and never computes it.
///
/// <para><b>Null until measured, and that is the contract, not a gap.</b>
/// <c>RoundFacts.Combine</c> takes the FIRST non-null answer, and the loop reads all-null as
/// retrievable — "the packet's default true until REACH-1 supplies it". So this source answers
/// null while there is no target and while no audit has run, and only then starts answering
/// true/false. A source that guessed <c>false</c> before it had measured anything would refuse
/// every Confirm in every suite that does not know about it; one that guessed <c>true</c> would
/// be a layer that silently does nothing.</para>
///
/// <para><b>Measured on two events, never per tick</b> (§5b, verbatim): when the target latches
/// Resting (<see cref="PropManager.RestLatched"/>) and once more on the Confirm press
/// (<see cref="IConfirmTimeAudit"/>). Twenty-four candidate points with up to two rays each is
/// not a per-frame cost, and it does not need to be one: nothing about the object's surroundings
/// changes without something coming to rest.</para>
///
/// <para><b>Layer 2 is an input, not a second opinion.</b> Whether the object is inside static
/// geometry is read off the prop manager's last rest audit rather than re-derived here — two
/// answers to one question is exactly what <see cref="PlacementIntegrity"/>'s own header warns
/// against, and the one that would rot is the copy.</para>
/// </summary>
public sealed class ReachabilityFactSource : IRoundFactSource, IConfirmTimeAudit
{
    /// <summary>The prefix every line this source prints carries. Greppable from a suite log.</summary>
    public const string LogPrefix = "[reach]";

    /// <summary>The loud line §5b asks for when the worst case happens anyway. Its exact text is
    /// the packet's, because a playtest proves the design did NOT hit this case by grepping for
    /// it and finding nothing.</summary>
    public const string SeekingBreachLine = "REACH-1 target unreachable during Seeking";

    private readonly PropManager _props;
    private readonly System.Func<HideSeekPhase> _phase;

    private int _targetPropId = -1;
    private Reachability.ReachVerdict _verdict;
    private bool _measured;
    private bool _inConfirmAudit;
    private int _audits;

    /// <summary>
    /// </summary>
    /// <param name="props">The server's prop manager. Subscribed to for rest latches; also the
    /// source of the prop list the sampler excludes and classifies against.</param>
    /// <param name="phase">The round's current phase, as a lambda rather than a reference to the
    /// driver — this source is constructed before the driver has stepped once, and a phase read
    /// through a closure cannot go stale the way a captured value would.</param>
    public ReachabilityFactSource(PropManager props, System.Func<HideSeekPhase> phase)
    {
        _props = props;
        _phase = phase;
        _props.RestLatched += OnRestLatched;
    }

    /// <summary>The prop the round is hiding, or -1 for none. Set by <c>--reach-target</c> today
    /// and by BTN-1's object rack when it lands; changing it drops the previous measurement,
    /// because a fact about one crate is not a fact about another.</summary>
    public int TargetPropId
    {
        get => _targetPropId;
        set
        {
            if (_targetPropId == value)
                return;
            _targetPropId = value;
            _measured = false;
            _verdict = default;
            GD.Print($"{LogPrefix} target prop is now {(_targetPropId < 0 ? "none" : _targetPropId.ToString())}");
        }
    }

    /// <summary>The last verdict, for the log, the self-test and the handoff.</summary>
    public Reachability.ReachVerdict Verdict => _verdict;

    /// <summary>How many full evaluations have run. The "never every tick" claim is checkable
    /// rather than asserted: a suite compares this against the number of settle events.</summary>
    public int AuditCount => _audits;

    // --- IRoundFactSource ---------------------------------------------------------------------

    /// <inheritdoc/>
    public bool? TargetRetrievable => _measured ? _verdict.Reachable : null;

    /// <inheritdoc/>
    public bool HostPressedStart => false;

    /// <inheritdoc/>
    public bool HiderHeldRackProp => false;

    /// <inheritdoc/>
    public bool HiderPressedConfirm => false;

    /// <inheritdoc/>
    public bool HiderHoldsTarget => false;

    /// <inheritdoc/>
    public bool TargetInDropOff => false;

    /// <inheritdoc/>
    public int SortsCompleted => 0;

    /// <inheritdoc/>
    public bool AnyPressedEnd => false;

    /// <inheritdoc/>
    public void AfterStep() { }

    // --- IConfirmTimeAudit --------------------------------------------------------------------

    /// <inheritdoc/>
    ///
    /// <para><b>Layer 2 first, then layer 3.</b> §5b's Confirm rule is "the target must be
    /// Resting, must pass layer 2, and must be grab-reachable", in that order — so the press
    /// re-runs the rest audit before it measures reachability. Without it, a hider could set the
    /// object down legally, shoulder it into a shelf back on the way out, and press Confirm on a
    /// world whose last audit predates the shove. <see cref="PropManager.ServerAuditRest"/> is a
    /// no-op on a held prop, which is the one case this could otherwise reach into (and which the
    /// loop refuses with <c>PutTheObjectDownFirst</c> anyway).</para>
    public void AuditBeforeConfirm()
    {
        if (_targetPropId >= 0)
        {
            // The audit raises RestLatched, which would evaluate a second time for no new
            // information; the latch handler stands down while this flag is up and the one
            // evaluation below is the one the press gets.
            _inConfirmAudit = true;
            try { _props.ServerAuditRest(_targetPropId); }
            finally { _inConfirmAudit = false; }
        }
        Evaluate("confirm");
    }

    // --- the audit ----------------------------------------------------------------------------

    private void OnRestLatched(int propId, RestAudit.Result audit)
    {
        if (propId != _targetPropId || _inConfirmAudit)
            return;
        Evaluate("rest");

        // §5b: layer 2 keeps running during Seeking, and the one case the design must never hit
        // is the target becoming unreachable AFTER the seek has begun — a seeker who knocks a
        // shelf onto it. There is no rescue beyond the audit in the MVP, so what this can do is
        // make the event impossible to miss in a log.
        if (_measured && !_verdict.Reachable && _phase() == HideSeekPhase.Seeking)
        {
            GD.PushWarning($"{LogPrefix} {SeekingBreachLine} — prop={propId} "
                           + $"{_verdict} after layer2 {audit.Reason}/{audit.Outcome}");
            GD.Print($"{LogPrefix} {SeekingBreachLine} — prop={propId} {_verdict}");
        }
    }

    /// <summary>One evaluation. <paramref name="why"/> names the event that asked, so the log
    /// shows the two triggers §5b allows and would show a third if anybody ever added one.</summary>
    private void Evaluate(string why)
    {
        if (_targetPropId < 0)
        {
            _measured = false;
            return;
        }
        NetworkedProp? target = _props.NodeFor(_targetPropId);
        if (target is null)
        {
            // The target id names nothing. Unmeasured, not unreachable: refusing Confirm because
            // a level author typo'd a prop id would be a silent-looking refusal with a sentence
            // that is not true.
            _measured = false;
            GD.PushWarning($"{LogPrefix} target prop {_targetPropId} does not exist — "
                           + "TargetRetrievable stays unmeasured");
            return;
        }

        bool insideStatic = _props.LastAuditFor(_targetPropId) is { } audit && audit.StuckInStatic;
        var sampler = new PhysicsReachSampler(target, _props.LiveProps, insideStatic);
        _verdict = Reachability.Evaluate(sampler);
        _measured = true;
        _audits++;
        GD.Print($"{LogPrefix} layer3 [{why}] prop={_targetPropId} {_verdict} "
                 + $"probe={sampler.ProbeDescription} queries={sampler.Queries}");
    }
}
