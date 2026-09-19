using System;
using System.Linq;
using MpFoundation.Game.Presentation;
using Xunit;
using RunT = Sail.Game.Run;
using UiT = MpFoundation.Ui.Flow;

namespace SailNet.Tests;

/// <summary>
/// CORE-INT-1 scope 2: the adapter seam between the real spine types and B1's UI mirrors.
///
/// The enum-agreement tests are the drift tripwire B1's D1 asked for: the mirror ordinals
/// are wire values ("append-only; ordinals cross the wire", spec §1.6), and the adapter
/// maps them with a bare byte cast — so if EITHER side ever renames, reorders or appends
/// asymmetrically, these tests go red before any screen shows a wrong state.
///
/// The demand tests pin the CORE-INT-1 demand-integer ruling (B1 parked question 1): the
/// real wire carries per-round increments where B1 assumed cumulative; FlowContract
/// translates, and the translation must equal the server's own cumulative arithmetic
/// (QuotaMath) for every round — ledger and screens agree by construction.
/// </summary>
public class FlowAdapterTests
{
    // --- enum agreement, byte for byte ------------------------------------------------------

    [Fact]
    public void PlaythroughState_MirrorAgreesByteForByte_BothDirections()
    {
        var real = Enum.GetValues<RunT.PlaythroughState>().Cast<RunT.PlaythroughState>().ToArray();
        var view = Enum.GetValues<UiT.PlaythroughState>().Cast<UiT.PlaythroughState>().ToArray();

        // Same member count — an appended value on one side only is drift, not a cast issue.
        Assert.Equal(real.Length, view.Length);

        foreach (RunT.PlaythroughState r in real)
        {
            // Same NAME exists on the view side...
            Assert.True(Enum.TryParse(r.ToString(), out UiT.PlaythroughState v),
                $"view enum is missing '{r}'");
            // ...at the same byte ordinal (the wire value).
            Assert.Equal((byte)r, (byte)v);
            // And the adapter's cast maps it to exactly that member.
            Assert.Equal(v, FlowContract.ToView(r));
        }
    }

    [Fact]
    public void RunOutcomeKind_MirrorAgreesByteForByte()
    {
        var real = Enum.GetValues<RunT.RunOutcomeKind>().Cast<RunT.RunOutcomeKind>().ToArray();
        var view = Enum.GetValues<UiT.RunOutcomeKind>().Cast<UiT.RunOutcomeKind>().ToArray();
        Assert.Equal(real.Length, view.Length);
        foreach (RunT.RunOutcomeKind r in real)
        {
            Assert.True(Enum.TryParse(r.ToString(), out UiT.RunOutcomeKind v),
                $"view enum is missing '{r}'");
            Assert.Equal((byte)r, (byte)v);
            Assert.Equal(v, FlowContract.ToView(r));
        }
    }

    // --- record mapping ---------------------------------------------------------------------

    [Fact]
    public void RunOutcome_MapsFieldForField()
    {
        var real = new RunT.RunOutcome(RunT.RunOutcomeKind.QuotaMissed, Round: 7, Demand: 41, Banked: 39);
        UiT.RunOutcome view = FlowContract.ToView(real);
        Assert.Equal(UiT.RunOutcomeKind.QuotaMissed, view.Kind);
        Assert.Equal(7, view.Round);
        Assert.Equal(41, view.Demand);
        Assert.Equal(39, view.Banked);
    }

    [Fact]
    public void RoundSummary_MapsCumulativeFieldsAndTranslatesNextDemand()
    {
        // Wire semantics (measured from PlaythroughDriver.ExecuteCommit): Demand/Banked
        // cumulative, NextDemand = DemandFor(round + 1), the increment.
        var real = new RunT.RoundSummary(Round: 1, Demand: 3, Banked: 5, NextDemand: 5);
        UiT.RoundSummary view = FlowContract.ToView(real);
        Assert.Equal(1, view.Round);
        Assert.Equal(3, view.Demand);
        Assert.Equal(5, view.Banked);
        // Cumulative-next = 3 + 5 = 8 — the exact figure B1's scripted demo hardcodes as
        // DemandRound2Cumulative, so demo and live arc word the same tally line.
        Assert.Equal(ScriptedPlaythrough.DemandRound2Cumulative, view.NextDemand);
    }

    // --- the demand-integer agreement: adapter arithmetic == the server's own ledger math ---

    [Fact]
    public void CumulativeNextDemand_EqualsQuotaMathCumulative_ForEveryEarlyAndTailRound()
    {
        int[] early = RunT.QuotaMath.FallbackEarlyRounds;
        const float factor = 1.35f;
        for (int round = 1; round <= 40; round++)
        {
            int cumThroughRound = RunT.QuotaMath.CumulativeDemand(round, early, factor);
            int nextIncrement = RunT.QuotaMath.Demand(round + 1, early, factor);
            int translated = FlowContract.CumulativeNextDemand(cumThroughRound, nextIncrement);
            Assert.Equal(RunT.QuotaMath.CumulativeDemand(round + 1, early, factor), translated);
        }
    }

    [Fact]
    public void CumulativeNextDemand_ClampsAtTheLedgersOwnBound_NeverWraps()
    {
        int clamp = RunT.QuotaMath.DemandClamp;
        Assert.Equal(clamp, FlowContract.CumulativeNextDemand(clamp, clamp));
        Assert.Equal(clamp, FlowContract.CumulativeNextDemand(clamp, 1));
        Assert.Equal(clamp,
            FlowContract.CumulativeNextDemand(int.MaxValue, int.MaxValue)); // long path, no wrap
        // A negative increment (defensive; QuotaMath floors at 1) never shrinks the figure.
        Assert.Equal(10, FlowContract.CumulativeNextDemand(10, -5));
    }
}
