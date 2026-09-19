using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>How several <see cref="IRoundFactSource"/>s become one <see cref="HideSeekInput"/>.</b>
///
/// <para>Pulled out of <c>HideSeekDriver</c> and kept engine-free on purpose: this is the seam
/// where five independent lanes' answers get reconciled, so it is the seam most likely to be got
/// wrong, and a rule that only executes inside a running scene tree is a rule no test can hold.
/// Same split <c>RunPhaseTracker</c> has from <c>RunDriver</c>, for the same reason.</para>
/// </summary>
public static class RoundFacts
{
    /// <summary>
    /// Combines every source's answer with <paramref name="roster"/> into the one input the loop
    /// steps on. The rules, restated here because this is where they execute:
    ///
    /// <list type="bullet">
    /// <item><b>Bools are OR'd.</b> A source that does not know a fact answers false and can never
    /// veto a source that does. That is also what makes the dev script safe to leave in — it can
    /// add a press, never swallow one.</item>
    /// <item><b><see cref="IRoundFactSource.TargetRetrievable"/> is the first NON-NULL answer</b>,
    /// in registration order. All null means nobody has measured it, and the loop reads that as
    /// retrievable (the packet's "default true until REACH-1 supplies it"). OR-ing it would be
    /// wrong in both directions: a source answering false is a REFUSAL, and one answering null is
    /// not a yes.</item>
    /// <item><b><see cref="IRoundFactSource.TowersCompleted"/> is the MAXIMUM.</b> It is absolute,
    /// so summing two sources that both know would double it, and taking the last would let a
    /// source that does not know zero it out.</item>
    /// </list>
    /// </summary>
    public static HideSeekInput Combine(IReadOnlyList<IRoundFactSource> sources,
        IReadOnlyList<int> roster)
    {
        bool start = false, rack = false, confirm = false, holds = false, bin = false, end = false;
        bool? retrievable = null;
        int towers = 0;

        if (sources is not null)
        {
            foreach (IRoundFactSource source in sources)
            {
                if (source is null)
                    continue;
                start |= source.HostPressedStart;
                rack |= source.HiderHeldRackProp;
                confirm |= source.HiderPressedConfirm;
                holds |= source.HiderHoldsTarget;
                bin |= source.TargetInDropOff;
                end |= source.AnyPressedEnd;
                retrievable ??= source.TargetRetrievable;
                towers = Math.Max(towers, source.TowersCompleted);
            }
        }

        return new HideSeekInput
        {
            HumanPeerIds = roster is null
                ? ImmutableArray<int>.Empty
                : ImmutableArray.CreateRange(roster),
            HostPressedStart = start,
            HiderHeldRackProp = rack,
            HiderPressedConfirm = confirm,
            HiderHoldsTarget = holds,
            TargetRetrievable = retrievable,
            TargetInDropOff = bin,
            TowersCompleted = towers,
            AnyPressedEnd = end,
        };
    }
}
