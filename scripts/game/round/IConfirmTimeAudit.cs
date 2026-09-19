namespace MpFoundation.Game.Round;

/// <summary>
/// <b>"Re-measure before you answer this one."</b> An <see cref="IRoundFactSource"/> may also
/// implement this when one of its facts is expensive enough that it is CACHED between events
/// rather than computed per tick, and the Confirm press is an event that must not read a stale
/// copy.
///
/// <para>REACH-1 is the case it exists for and, today, the only implementer. §5b says the
/// grab-reachability audit runs "when the target latches Resting and once more on the Confirm
/// press; never every tick" — the first half is an event the prop manager raises, and this is the
/// second half. Without it the hider could set the object down, have it settle legally, then
/// nudge it into a shelf back with their body on the way out and press Confirm on a fact measured
/// before the nudge.</para>
///
/// <para><b>The driver calls it BEFORE the loop steps, and then re-collects the facts</b>, so the
/// value the loop refuses (or does not refuse) on is the one this call produced. Collecting once
/// and auditing afterwards would be a whole round behind, which is the same bug in a slower
/// costume.</para>
///
/// <para><b>It is an optional side interface, not a member of <see cref="IRoundFactSource"/>.</b>
/// BTN-1, TASK-1 and the dev script all answer instantly from a latch and have nothing to
/// re-measure; putting an empty method on all of them would be four implementations of "no" so
/// that one lane could say "yes".</para>
/// </summary>
public interface IConfirmTimeAudit
{
    /// <summary>Called on the tick the hider's Confirm press was seen, before
    /// <c>HideSeekLoop.Step</c> reads any fact. Cheap or not, it runs once per press.</summary>
    void AuditBeforeConfirm();
}
