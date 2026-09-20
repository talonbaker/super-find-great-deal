using System.Collections.Generic;
using MpFoundation.Game.Round;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>BTN-1: the lamp derivation and the press → fact mapping, without an engine.</b>
///
/// <para>Everything the three round buttons DECIDE lives in <see cref="RoundButtonRules"/>,
/// <see cref="RoundButtonText"/> and <see cref="RoundControlFacts"/>, which is what makes this
/// file possible: no scene tree, no server, no socket. What is left in the Godot half is meshes,
/// sounds, an RPC funnel and an <c>Area3D</c> — and that is what
/// <c>tests/Run-ButtonsTest.ps1</c> proves on a real server with two real bots.</para>
///
/// <para>The split is deliberate and it is the same one <c>Reachability</c> /
/// <c>PhysicsReachSampler</c> uses one lane over: the rule is the part worth a test and the part
/// that rots silently.</para>
/// </summary>
public class RoundButtonTests
{
    private const int Hider = 11;
    private const int Seeker = 22;

    private static RoundButtonRules.LampFacts Facts(HideSeekPhase phase, int self,
        int humans = 2, bool rack = false, bool target = false, bool synced = true) =>
        new(synced, phase, self, Hider, Seeker, humans, rack, target);

    // =========================================================================================
    // The lamp
    // =========================================================================================

    /// <summary>The affordance's whole point: START is lit exactly when the loop's Holding branch
    /// would accept. Two humans, both roles filled, hider holding something off the rack.</summary>
    [Fact]
    public void StartLamp_LitOnlyWhenTheLoopWouldAccept()
    {
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Facts(HideSeekPhase.Holding, Hider, humans: 2, rack: true)));
        // The seeker sees the same lamp. It is a fact about the ROUND, not about who is looking:
        // either player may press Start, so a lamp that was dark for one of the two people in
        // front of it would be lying to one of them.
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Facts(HideSeekPhase.Holding, Seeker, humans: 2, rack: true)));
    }

    [Theory]
    // The four ways a Start would be refused, each one Dark before the press.
    [InlineData(HideSeekPhase.Holding, 1, true)]    // one human
    [InlineData(HideSeekPhase.Holding, 3, true)]    // three humans — exactly two, not at least
    [InlineData(HideSeekPhase.Holding, 2, false)]   // empty hands
    [InlineData(HideSeekPhase.Hiding, 2, true)]     // the round is already running
    [InlineData(HideSeekPhase.Seeking, 2, true)]
    [InlineData(HideSeekPhase.Together, 2, true)]
    [InlineData(HideSeekPhase.Tally, 2, true)]
    public void StartLamp_DarkWheneverAPressWouldBeRefused(HideSeekPhase phase, int humans, bool rack)
    {
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start,
            Facts(phase, Hider, humans, rack)));
    }

    /// <summary>A vacant role is Dark even with two bodies in the room — the loop refuses that
    /// case with <c>NeedTwoPlayers</c>, so the lamp must agree.</summary>
    [Fact]
    public void StartLamp_DarkWhenARoleIsVacant()
    {
        var noHider = new RoundButtonRules.LampFacts(true, HideSeekPhase.Holding, Seeker,
            0, Seeker, 2, true, false);
        var noSeeker = new RoundButtonRules.LampFacts(true, HideSeekPhase.Holding, Hider,
            Hider, 0, 2, true, false);
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start, noHider));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start, noSeeker));
    }

    /// <summary>Confirm is the HIDER's, while hiding, with the object put down.</summary>
    [Fact]
    public void ConfirmLamp_LitOnlyForTheHiderWithEmptyHands()
    {
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Facts(HideSeekPhase.Hiding, Hider, target: false)));
        // Still carrying it: the loop refuses with PutTheObjectDownFirst.
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Facts(HideSeekPhase.Hiding, Hider, target: true)));
        // The seeker is not entitled to this one even if they could reach it.
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Facts(HideSeekPhase.Hiding, Seeker)));
        // Wrong phase.
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Facts(HideSeekPhase.Holding, Hider)));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm,
            Facts(HideSeekPhase.Seeking, Hider)));
    }

    [Fact]
    public void EndLamp_LitOnlyInTogether_ForEitherPlayer()
    {
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.End,
            Facts(HideSeekPhase.Together, Hider)));
        Assert.Equal(RoundLamp.Lit, RoundButtonRules.Lamp(RoundButtonKind.End,
            Facts(HideSeekPhase.Together, Seeker)));
        foreach (HideSeekPhase phase in new[]
                 {
                     HideSeekPhase.Holding, HideSeekPhase.Hiding,
                     HideSeekPhase.Seeking, HideSeekPhase.Tally,
                 })
        {
            Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.End,
                Facts(phase, Seeker)));
        }
    }

    /// <summary>
    /// <b>An unsynced peer's lamps are all Dark.</b> The trap this closes is the one every
    /// Synced flag in this codebase exists for: <c>HideSeekDriver</c>'s zero value is "Holding,
    /// round 1, no roles", which is a perfectly plausible sentence and, on a peer that has not
    /// heard from the server yet, a wrong one. Without this branch a client that joined a second
    /// ago would light START at a player who is mid-seek.
    /// </summary>
    [Fact]
    public void EveryLamp_IsDarkBeforeTheRoundHasSynced()
    {
        var unsynced = new RoundButtonRules.LampFacts(false, HideSeekPhase.Holding, Hider,
            Hider, Seeker, 2, true, false);
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Start, unsynced));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.Confirm, unsynced));
        Assert.Equal(RoundLamp.Dark, RoundButtonRules.Lamp(RoundButtonKind.End, unsynced));
    }

    // =========================================================================================
    // The gate
    // =========================================================================================

    /// <summary>
    /// <b>The gate is NARROWER than the lamp, and this is the test that says so.</b> A Start
    /// pressed with one player, or with the hider's hands empty, is FORWARDED — the round refuses
    /// it and its own sentence comes back. If the gate duplicated the loop's conditions there
    /// would be two authorities on the same question, and the packet's whole affordance argument
    /// (a Dark button tells you not to bother; the reason text tells you why when you do anyway)
    /// would collapse into a button that simply does nothing.
    /// </summary>
    [Fact]
    public void Gate_ForwardsEveryCaseTheLoopItselfRefuses()
    {
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Hider, Hider, seekerPeerId: 0));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Hider, Hider, Seeker));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Confirm, HideSeekPhase.Hiding, Hider, Hider, Seeker));
    }

    [Theory]
    [InlineData(RoundButtonKind.Start, HideSeekPhase.Hiding)]
    [InlineData(RoundButtonKind.Start, HideSeekPhase.Seeking)]
    [InlineData(RoundButtonKind.Start, HideSeekPhase.Together)]
    [InlineData(RoundButtonKind.Start, HideSeekPhase.Tally)]
    [InlineData(RoundButtonKind.Confirm, HideSeekPhase.Holding)]
    [InlineData(RoundButtonKind.Confirm, HideSeekPhase.Seeking)]
    [InlineData(RoundButtonKind.End, HideSeekPhase.Holding)]
    [InlineData(RoundButtonKind.End, HideSeekPhase.Hiding)]
    [InlineData(RoundButtonKind.End, HideSeekPhase.Seeking)]
    [InlineData(RoundButtonKind.End, HideSeekPhase.Tally)]
    public void Gate_RefusesAPressInTheWrongPhase(RoundButtonKind kind, HideSeekPhase phase)
    {
        Assert.Equal(PressRefusal.NotNow,
            RoundButtonRules.Gate(kind, phase, Hider, Hider, Seeker));
    }

    [Fact]
    public void Gate_ConfirmIsTheHidersAndEndIsEitherPlayers()
    {
        Assert.Equal(PressRefusal.NotYourButton, RoundButtonRules.Gate(
            RoundButtonKind.Confirm, HideSeekPhase.Hiding, Seeker, Hider, Seeker));
        Assert.Equal(PressRefusal.NotYourButton, RoundButtonRules.Gate(
            RoundButtonKind.Confirm, HideSeekPhase.Hiding, 0, Hider, Seeker));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.End, HideSeekPhase.Together, Seeker, Hider, Seeker));
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.End, HideSeekPhase.Together, Hider, Hider, Seeker));
        // Either player may Start — the fact is called HostPressedStart for historical reasons
        // and the loop never asks who pressed it.
        Assert.Equal(PressRefusal.None, RoundButtonRules.Gate(
            RoundButtonKind.Start, HideSeekPhase.Holding, Seeker, Hider, Seeker));
    }

    // =========================================================================================
    // Reading the round's answer
    // =========================================================================================

    [Fact]
    public void ReadResult_APhaseThatMovedIsAnAcceptance()
    {
        (bool accepted, PressRefusal reason) = RoundButtonRules.ReadResult(
            HideSeekPhase.Holding, HideSeekPhase.Hiding, HideSeekRefusal.None);
        Assert.True(accepted);
        Assert.Equal(PressRefusal.None, reason);
    }

    [Theory]
    [InlineData(HideSeekRefusal.NeedTwoPlayers, PressRefusal.NeedTwoPlayers)]
    [InlineData(HideSeekRefusal.HiderMustHoldAnObject, PressRefusal.HiderMustHoldAnObject)]
    [InlineData(HideSeekRefusal.PutTheObjectDownFirst, PressRefusal.PutTheObjectDownFirst)]
    [InlineData(HideSeekRefusal.NobodyCouldReachThat, PressRefusal.NobodyCouldReachThat)]
    public void ReadResult_ForwardsTheRoundsOwnReasonVerbatim(HideSeekRefusal from, PressRefusal to)
    {
        (bool accepted, PressRefusal reason) = RoundButtonRules.ReadResult(
            HideSeekPhase.Holding, HideSeekPhase.Holding, from);
        Assert.False(accepted);
        Assert.Equal(to, reason);
    }

    /// <summary>The unreachable third case, pinned so it stays an ANSWER rather than becoming
    /// silence if ROUND-1's no-silent-no-op invariant is ever broken.</summary>
    [Fact]
    public void ReadResult_NeitherMovedNorRefused_StillAnswers()
    {
        (bool accepted, PressRefusal reason) = RoundButtonRules.ReadResult(
            HideSeekPhase.Holding, HideSeekPhase.Holding, HideSeekRefusal.None);
        Assert.False(accepted);
        Assert.Equal(PressRefusal.NotNow, reason);
    }

    // =========================================================================================
    // The copy
    // =========================================================================================

    /// <summary>
    /// <b>The ordinals 1–4 ARE <see cref="HideSeekRefusal"/>'s</b>, which is what makes
    /// <c>(PressRefusal)(byte)roundRefusal</c> the identity map and keeps ONE list of refusal
    /// sentences in the game. A renumber here would silently show the player a different reason
    /// than the server refused with — the exact failure ROUND-1's "values are stable" note is
    /// about, one layer up.
    /// </summary>
    [Fact]
    public void PressRefusal_MirrorsTheRoundsOrdinalsExactly()
    {
        Assert.Equal((byte)HideSeekRefusal.NeedTwoPlayers, (byte)PressRefusal.NeedTwoPlayers);
        Assert.Equal((byte)HideSeekRefusal.HiderMustHoldAnObject, (byte)PressRefusal.HiderMustHoldAnObject);
        Assert.Equal((byte)HideSeekRefusal.PutTheObjectDownFirst, (byte)PressRefusal.PutTheObjectDownFirst);
        Assert.Equal((byte)HideSeekRefusal.NobodyCouldReachThat, (byte)PressRefusal.NobodyCouldReachThat);
        Assert.Equal((byte)HideSeekRefusal.None, (byte)PressRefusal.None);
        // And the button's own three sit ABOVE the round's, so appending to either enum is safe.
        Assert.True((byte)PressRefusal.NotNow > (byte)HideSeekRefusal.NobodyCouldReachThat);
    }

    [Fact]
    public void Sentences_ForTheRoundsReasons_AreTheRoundsOwnWords()
    {
        foreach (PressRefusal r in new[]
                 {
                     PressRefusal.NeedTwoPlayers, PressRefusal.HiderMustHoldAnObject,
                     PressRefusal.PutTheObjectDownFirst, PressRefusal.NobodyCouldReachThat,
                 })
        {
            Assert.Equal(HideSeekText.RefusalSentence((HideSeekRefusal)(byte)r),
                RoundButtonText.Sentence(r, RoundButtonKind.Start));
        }
    }

    /// <summary>Every refusal a player can be shown has words on it. A blank sentence is the
    /// silent no-op wearing a costume.</summary>
    [Fact]
    public void EveryRefusalHasASentence_AndNoneIsEmpty()
    {
        foreach (PressRefusal r in System.Enum.GetValues<PressRefusal>())
        {
            foreach (RoundButtonKind k in System.Enum.GetValues<RoundButtonKind>())
            {
                string s = RoundButtonText.Sentence(r, k);
                if (r == PressRefusal.None)
                    Assert.Equal(string.Empty, s);
                else
                    Assert.False(string.IsNullOrWhiteSpace(s),
                        $"{r} on the {k} button has no sentence");
            }
        }
    }

    /// <summary><see cref="PressRefusal.NotNow"/> means three different things and says three
    /// different things. One sentence for all three would be the "invalid state" non-answer
    /// <c>INTERACTION-BIBLE.md</c> §5 names.</summary>
    [Fact]
    public void NotNow_SaysSomethingDifferentOnEachButton()
    {
        string start = RoundButtonText.Sentence(PressRefusal.NotNow, RoundButtonKind.Start);
        string confirm = RoundButtonText.Sentence(PressRefusal.NotNow, RoundButtonKind.Confirm);
        string end = RoundButtonText.Sentence(PressRefusal.NotNow, RoundButtonKind.End);
        Assert.NotEqual(start, confirm);
        Assert.NotEqual(confirm, end);
        Assert.NotEqual(start, end);
    }

    [Fact]
    public void Labels_AreThePacketsAndCarryNoKey()
    {
        Assert.Equal("START", RoundButtonText.Label(RoundButtonKind.Start));
        Assert.Equal("I'M DONE HIDING", RoundButtonText.Label(RoundButtonKind.Confirm));
        Assert.Equal("END ROUND", RoundButtonText.Label(RoundButtonKind.End));
        // The third of the three playtest failures: a prompt that never said which key, in a
        // build where the key is rebindable. The fix is that the LABEL never names one at all —
        // InteractPrompt resolves the live glyph. A label that grew an "E" would undo it.
        foreach (RoundButtonKind k in System.Enum.GetValues<RoundButtonKind>())
            Assert.DoesNotContain("PRESS", RoundButtonText.Label(k));
    }

    // =========================================================================================
    // The press → fact mapping
    // =========================================================================================

    [Theory]
    [InlineData(RoundButtonKind.Start)]
    [InlineData(RoundButtonKind.Confirm)]
    [InlineData(RoundButtonKind.End)]
    public void APress_SetsItsOwnFactAndNoOther(RoundButtonKind kind)
    {
        var facts = new RoundControlFacts();
        facts.Press(kind, Hider, HideSeekPhase.Holding);

        Assert.Equal(kind == RoundButtonKind.Start, facts.HostPressedStart);
        Assert.Equal(kind == RoundButtonKind.Confirm, facts.HiderPressedConfirm);
        Assert.Equal(kind == RoundButtonKind.End, facts.AnyPressedEnd);
        Assert.Equal(1, facts.PressesLatched);
    }

    /// <summary><b>Where an edge dies.</b> Without this, one Start press is re-consumed on every
    /// tick for the rest of the session — the failure <c>IRoundFactSource.AfterStep</c>'s own
    /// doc names.</summary>
    [Fact]
    public void AfterStep_ClearsEveryPress()
    {
        var facts = new RoundControlFacts();
        facts.Press(RoundButtonKind.Start, Hider, HideSeekPhase.Holding);
        facts.Press(RoundButtonKind.End, Seeker, HideSeekPhase.Together);
        facts.AfterStep();
        Assert.False(facts.HostPressedStart);
        Assert.False(facts.AnyPressedEnd);
        Assert.False(facts.HiderPressedConfirm);
    }

    /// <summary>
    /// <b>No debounce, and the loop's one-shot reading is the idempotence.</b> The packet is
    /// explicit, and the reason is scar tissue: a debounce mistaken for a confirmation is the
    /// first of the three playtests the one shipped in-world control failed. Ten presses in one
    /// tick are one fact and one transition — and, because the presser has to be told something,
    /// exactly one ANSWER per (button, peer).
    /// </summary>
    [Fact]
    public void TenPressesInOneTick_AreOneFactAndOneAnswer()
    {
        var facts = new RoundControlFacts();
        IReadOnlyList<RoundControlFacts.PendingPress>? seen = null;
        facts.Adjudicate = p => seen = new List<RoundControlFacts.PendingPress>(p);

        for (int i = 0; i < 10; i++)
            facts.Press(RoundButtonKind.Start, Hider, HideSeekPhase.Holding);

        Assert.True(facts.HostPressedStart);
        Assert.Equal(10, facts.PressesLatched);   // every one of them was really taken
        facts.AfterStep();
        Assert.NotNull(seen);
        Assert.Single(seen!);                      // and the presser is told once
        Assert.Equal(Hider, seen![0].PeerId);
        Assert.Equal(HideSeekPhase.Holding, seen[0].PhaseAtPress);
    }

    /// <summary>Two DIFFERENT players pressing the same button both get an answer: a press with
    /// no result on the presser is this packet's stated defect, and "somebody else pressed it
    /// first" is not an exemption.</summary>
    [Fact]
    public void TwoPlayersPressingTheSameButton_BothGetAnAnswer()
    {
        var facts = new RoundControlFacts();
        IReadOnlyList<RoundControlFacts.PendingPress>? seen = null;
        facts.Adjudicate = p => seen = new List<RoundControlFacts.PendingPress>(p);
        facts.Press(RoundButtonKind.End, Hider, HideSeekPhase.Together);
        facts.Press(RoundButtonKind.End, Seeker, HideSeekPhase.Together);
        facts.AfterStep();
        Assert.Equal(2, seen!.Count);
    }

    /// <summary>
    /// <b>Answers are delivered before anything is cleared.</b> <see cref="RoundControlFacts.Stepped"/>
    /// is where <c>RoundControls</c> hangs the round-boundary clear, and a clear that ran first
    /// would swallow the answer to a press made on the reset tick.
    /// </summary>
    [Fact]
    public void TheRoundBoundaryClear_DoesNotSwallowThatTicksAnswer()
    {
        var facts = new RoundControlFacts();
        var order = new List<string>();
        facts.Adjudicate = p =>
        {
            order.Add($"adjudicate:{p.Count}");
        };
        facts.Stepped = () =>
        {
            order.Add("stepped");
            facts.ClearForNewRound();
        };
        facts.Press(RoundButtonKind.Start, Hider, HideSeekPhase.Holding);
        facts.AfterStep();
        Assert.Equal(new[] { "adjudicate:1", "stepped" }, order);
    }

    /// <summary>The level facts are PROBED at the instant the loop folds, never cached from this
    /// node's own tick — which is what removes any ordering dependency between two nodes'
    /// <c>_PhysicsProcess</c>. A null probe answers false, so a world with no rack and no bin
    /// (every CI world) is silent rather than special-cased.</summary>
    [Fact]
    public void LevelFacts_ComeFromTheProbeEveryTimeTheyAreRead()
    {
        var facts = new RoundControlFacts();
        Assert.False(facts.HiderHeldRackProp);
        Assert.False(facts.HiderHoldsTarget);
        Assert.False(facts.TargetInDropOff);

        bool rack = false;
        int reads = 0;
        facts.RackHoldProbe = () => { reads++; return rack; };
        Assert.False(facts.HiderHeldRackProp);
        rack = true;
        Assert.True(facts.HiderHeldRackProp);
        Assert.Equal(2, reads);
    }

    /// <summary>This lane must never win REACH-1's first-non-null race, and must never claim
    /// TASK-1's towers.</summary>
    [Fact]
    public void ItAnswersNothingThatBelongsToAnotherLane()
    {
        var facts = new RoundControlFacts();
        Assert.Null(facts.TargetRetrievable);
        Assert.Equal(0, facts.TowersCompleted);
    }

    // =========================================================================================
    // Through the real combiner and the real loop
    // =========================================================================================

    /// <summary>
    /// <b>End to end without an engine:</b> a press on the real fact source, through the real
    /// <see cref="RoundFacts.Combine"/>, into the real <see cref="HideSeekLoop"/>. This is the
    /// assertion that would catch a fact wired to the wrong field — every test above it would
    /// stay green if <c>HostPressedStart</c> were returned from the Confirm latch.
    /// </summary>
    [Fact]
    public void AStartPress_StartsTheRound_ThroughTheRealCombinerAndLoop()
    {
        var facts = new RoundControlFacts { RackHoldProbe = () => true };
        var sources = new List<IRoundFactSource> { facts };
        int[] roster = { Hider, Seeker };
        HideSeekTuning tuning = HideSeekTuning.Default;

        HideSeekState s = HideSeekLoop.Restart(tuning);
        // One quiet tick so the roles get assigned from the roster.
        s = HideSeekLoop.Step(s, RoundFacts.Combine(sources, roster), 1f / 60f, tuning);
        facts.AfterStep();
        Assert.Equal(HideSeekPhase.Holding, s.Phase);

        facts.Press(RoundButtonKind.Start, s.HiderPeerId, s.Phase);
        s = HideSeekLoop.Step(s, RoundFacts.Combine(sources, roster), 1f / 60f, tuning);
        facts.AfterStep();

        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
        Assert.Equal(HideSeekRefusal.None, s.Refusal);

        // And the edge really died: the next tick must not start the round a second time.
        s = HideSeekLoop.Step(s, RoundFacts.Combine(sources, roster), 1f / 60f, tuning);
        facts.AfterStep();
        Assert.Equal(HideSeekPhase.Hiding, s.Phase);
    }

    /// <summary>
    /// <b>The refused Start the smoke stages, proved here first.</b> One human in the room: the
    /// press is forwarded (the gate only tests phase and role), the loop names
    /// <c>NeedTwoPlayers</c>, and <see cref="RoundButtonRules.ReadResult"/> turns that into the
    /// sentence the presser reads.
    /// </summary>
    [Fact]
    public void AStartPressWithOnePlayer_IsRefusedWithWordsRatherThanSilence()
    {
        var facts = new RoundControlFacts { RackHoldProbe = () => true };
        var sources = new List<IRoundFactSource> { facts };
        int[] alone = { Hider };
        HideSeekTuning tuning = HideSeekTuning.Default;

        HideSeekState s = HideSeekLoop.Restart(tuning);
        s = HideSeekLoop.Step(s, RoundFacts.Combine(sources, alone), 1f / 60f, tuning);
        facts.AfterStep();

        Assert.Equal(PressRefusal.None,
            RoundButtonRules.Gate(RoundButtonKind.Start, s.Phase, Hider, s.HiderPeerId, s.SeekerPeerId));

        HideSeekPhase before = s.Phase;
        facts.Press(RoundButtonKind.Start, Hider, before);
        s = HideSeekLoop.Step(s, RoundFacts.Combine(sources, alone), 1f / 60f, tuning);

        (bool accepted, PressRefusal reason) = RoundButtonRules.ReadResult(before, s.Phase, s.Refusal);
        Assert.False(accepted);
        Assert.Equal(PressRefusal.NeedTwoPlayers, reason);
        Assert.Equal("TWO PLAYERS ARE NEEDED TO START",
            RoundButtonText.Sentence(reason, RoundButtonKind.Start));
        Assert.Equal(HideSeekPhase.Holding, s.Phase);
    }

    // =========================================================================================
    // The buzzer
    // =========================================================================================

    /// <summary>
    /// The bin's rejection buzzer, asserted the way <c>Triumph</c> and <c>GooseHonk</c> are: raw
    /// PCM, no audio device. <b>Headroom is the point</b> — <c>SfxLab.Render</c> clamps to ±1, so
    /// a recipe that peaks at the rail is silently flattened, and a flattened buzz is a crunch.
    /// </summary>
    [Fact]
    public void BuzzerPcm_IsShortAndHasHeadroom()
    {
        float[] pcm = SfxLab.BuzzerPcm();
        Assert.InRange(pcm.Length, 48000 * 0.2, 48000 * 0.35);   // ~0.26 s at 48 kHz

        float peak = 0f;
        foreach (float v in pcm)
            peak = System.Math.Max(peak, System.Math.Abs(v));
        Assert.InRange(peak, 0.1f, 0.95f);

        // It FALLS, and that is the design: a rising interval reads as a question and this is an
        // answer. Compared as energy in the two halves' zero crossings rather than by an FFT —
        // a lower tone crosses zero fewer times over the same window.
        int firstHalfCrossings = ZeroCrossings(pcm, 0, pcm.Length / 2);
        int secondHalfCrossings = ZeroCrossings(pcm, pcm.Length / 2, pcm.Length);
        Assert.True(secondHalfCrossings < firstHalfCrossings,
            $"the buzzer must fall in pitch: {firstHalfCrossings} then {secondHalfCrossings} crossings");
    }

    private static int ZeroCrossings(float[] pcm, int from, int to)
    {
        int n = 0;
        for (int i = from + 1; i < to; i++)
            if ((pcm[i - 1] < 0f && pcm[i] >= 0f) || (pcm[i - 1] >= 0f && pcm[i] < 0f))
                n++;
        return n;
    }
}
