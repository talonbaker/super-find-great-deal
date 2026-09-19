using System;
using System.Collections.Generic;
using MpFoundation.Ui.Flow;

namespace MpFoundation.Game.Presentation;

/// <summary>
/// The canonical scripted playthrough CORE-PROG-B1's screens are demoed and tested against:
/// RoundIntro → Day → Dusk → Night → verdict → RoundEnd → UpgradeLobby → next round → Loss,
/// expressed as ordered steps over a <see cref="FakePlaythroughDriver"/>. ONE script, three
/// consumers — the xUnit walk (<c>FlowScreensTests</c>), the in-engine headless self-test
/// (<c>--screenflow-selftest</c>), and the human demo (<c>--screen-demo</c>) — so what the
/// orchestrator shows Talon is exactly what the tests proved.
///
/// Band crossings (Dusk/Night) are <see cref="StepCue"/>s, not state commits: Day/Dusk/Night
/// are <c>InRound × Band</c> in the spec (§1.2), and the band comes from the locked
/// <c>RunDriver</c> in live play. Here a cue tells the consumer to drive the telegraph
/// surface directly, the same way the crossing event would.
/// </summary>
public enum StepCue : byte
{
    None = 0,
    /// <summary>The DayToDusk crossing — the telegraph window opens (toast).</summary>
    Dusk = 1,
    /// <summary>The DuskToNight crossing — the nightfall treatment.</summary>
    Nightfall = 2,
}

public readonly record struct PlaythroughStep(string Name, StepCue Cue, Action<FakePlaythroughDriver>? Apply);

public static class ScriptedPlaythrough
{
    // Placeholder demand curve, spec §2.2's opening values [3, 5, 8, ...] made cumulative
    // (§2.1: the verdict checks CumulativeBanked against Σ demand(1..N)).
    public const int DemandRound1 = 3;
    public const int DemandRound2Cumulative = 8; // 3 + 5

    /// <summary>The walk. Round 1 is survived (with surplus, so the tally's carryover line
    /// shows); round 2 is lost (0 banked beyond carryover — the loss screen's honest-numbers
    /// extreme). Every §3.5 surface is exercised: connecting gate (pre-sync), intro card,
    /// quota strip (in-round, all stages), dusk toast + nightfall treatment, tally, lobby,
    /// second intro, loss.</summary>
    public static IReadOnlyList<PlaythroughStep> Steps { get; } = new PlaythroughStep[]
    {
        new("Connecting (pre-sync boot)", StepCue.None, null), // driver starts Boot/unsynced — the gate shows.
        new("Flow sync lands", StepCue.None, d => d.CommitSynced()),
        new("Round intro — NIGHT 1, demand announced", StepCue.None, d => d.CommitRoundIntro(1, DemandRound1)),
        new("Round live — day", StepCue.None, d => d.CommitRoundLive()),
        new("Wood banked at the drop-off (+2)", StepCue.None, d => d.CommitBank(2)),
        new("Dusk sweep — the telegraph", StepCue.Dusk, null),
        new("Night falls — what changes, spelled out", StepCue.Nightfall, null),
        new("More banked in the dark (+3, surplus)", StepCue.None, d => d.CommitBank(3)),
        new("Dawn verdict — round 1 survived", StepCue.None,
            d => d.CommitRoundEnd(new RoundSummary(Round: 1, Demand: DemandRound1, Banked: 5, NextDemand: DemandRound2Cumulative))),
        new("Upgrade lobby — stretch your legs", StepCue.None, d => d.CommitUpgradeLobby()),
        new("Round intro — NIGHT 2, demand escalates", StepCue.None, d => d.CommitRoundIntro(2, DemandRound2Cumulative)),
        new("Round live — day 2", StepCue.None, d => d.CommitRoundLive()),
        new("Night falls again", StepCue.Nightfall, null),
        new("Dawn verdict — the cache came up short", StepCue.None,
            d => d.CommitLoss(new RunOutcome(RunOutcomeKind.QuotaMissed, Round: 2, Demand: DemandRound2Cumulative, Banked: 5))),
    };
}
