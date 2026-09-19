namespace MpFoundation.Game.Round;

/// <summary>
/// <b>How another lane tells the round what it is seeing.</b> BTN-1's three buttons, CARRY-1's
/// hands, REACH-1's reachability audit, TASK-1's towers and the dev script all implement this and
/// register with <c>HideSeekDriver</c>; the driver combines them into one
/// <see cref="HideSeekInput"/> per sim tick.
///
/// <para><b>Facts, never decisions.</b> Nothing here says "start the round" — it says "the host
/// pressed the button". What that means is <see cref="HideSeekLoop"/>'s, and keeping the split
/// this sharp is what lets the whole round be tested without an engine.</para>
///
/// <para><b>How several sources combine</b> (and the rules are deliberately boring, because a
/// clever combination is a place for two lanes to disagree in a live session):</para>
/// <list type="bullet">
/// <item><b>Every bool is OR'd.</b> Presses and hands alike. A source that does not know about a
/// fact returns false and can never veto one that does. That is also what makes the dev script
/// safe to leave in: it can add a press, never swallow one.</item>
/// <item><b><see cref="TargetRetrievable"/> is the FIRST non-null answer</b>, in registration
/// order. It is nullable precisely so "nobody has measured this" is distinguishable from "no" —
/// with everything null the driver passes null and the loop reads it as retrievable, which is the
/// packet's "default true until REACH-1 supplies it".</item>
/// <item><b><see cref="TowersCompleted"/> is the MAXIMUM.</b> It is an absolute count, not an
/// increment, so summing two sources that both know would double it and taking the last would let
/// a source that does not know zero it. The maximum is the only combination where a source that
/// answers 0 costs nothing.</item>
/// </list>
///
/// <para><b><see cref="AfterStep"/> is where an edge dies.</b> A press is true for the one tick
/// the loop consumed it and the driver then says so; a source that latched a button press clears
/// its latch there. Without it, one Start press would be re-consumed on every tick for the rest of
/// the session.</para>
/// </summary>
public interface IRoundFactSource
{
    /// <summary>The host pressed Start since the last <see cref="AfterStep"/>.</summary>
    bool HostPressedStart { get; }

    /// <summary>The hider is holding an object taken off the rack.</summary>
    bool HiderHeldRackProp { get; }

    /// <summary>The hider pressed Confirm since the last <see cref="AfterStep"/>.</summary>
    bool HiderPressedConfirm { get; }

    /// <summary>The hider still has the target in their hands.</summary>
    bool HiderHoldsTarget { get; }

    /// <summary>Is the target grab-reachable where it sits? <c>null</c> = this source has not
    /// measured it. See the class doc.</summary>
    bool? TargetRetrievable { get; }

    /// <summary>The target prop is in the drop-off bin.</summary>
    bool TargetInDropOff { get; }

    /// <summary>Towers completed this round, absolute.</summary>
    int TowersCompleted { get; }

    /// <summary>Somebody pressed End since the last <see cref="AfterStep"/>.</summary>
    bool AnyPressedEnd { get; }

    /// <summary>Called once per sim tick, after the loop has consumed this tick's facts. Clear
    /// every latched press here.</summary>
    void AfterStep();
}

/// <summary>
/// <b>The source that knows nothing</b>, so <c>HideSeekDriver</c> has something to fold on a
/// server where no other lane has landed yet. Every answer is the resting one: no press, empty
/// hands, no measurement, nothing built.
///
/// <para>It exists rather than the driver special-casing an empty list because "runs standalone"
/// should be the same code path as "runs with five sources", not a branch that only the packet
/// that wrote it ever executes.</para>
/// </summary>
public sealed class NullRoundFactSource : IRoundFactSource
{
    /// <summary>Stateless, so one instance is enough.</summary>
    public static readonly NullRoundFactSource Instance = new();

    public bool HostPressedStart => false;
    public bool HiderHeldRackProp => false;
    public bool HiderPressedConfirm => false;
    public bool HiderHoldsTarget => false;
    public bool? TargetRetrievable => null;
    public bool TargetInDropOff => false;
    public int TowersCompleted => 0;
    public bool AnyPressedEnd => false;
    public void AfterStep() { }
}
