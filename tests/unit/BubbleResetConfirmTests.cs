using System;
using System.IO;
using Sail.Game.Bubble;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>LEVER-1: the warning on the reset lever, and the sentence it exists to make false.</b>
///
/// <para><b>The acceptance is Talon's own words</b> (2026-08-29, his playtest of the wave-1
/// bubble test): <i>"There needs to be a sign at least that it will reset the bubble counter and
/// a warning ... because I would be upset if I spent hours collecting all the bubbles and then
/// someone reset my progress."</i> The first test below is that sentence made executable — a full
/// board, a stray press from a peer who never confirmed, and the tally still standing. Its
/// companion is the bug that fixing it could easily have introduced: a deliberate confirmed press
/// must still reset, because a warning that blocks the real action is a different defect.</para>
///
/// <para><b>Why these run against <see cref="BubbleResetAdjudicator"/> rather than against the
/// two rule classes separately.</b> Each of those is exhaustively testable alone, and is tested
/// alone further down. What was not previously testable outside a scene tree and three processes
/// is the thing most likely to be got wrong: the ORDER. Driving the real adjudicator means these
/// tests would catch "the first press spent the debounce" — a change that leaves both rule
/// classes individually correct and the lever unusable.</para>
///
/// <para><b>The board is a plain int</b> and the one line that moves it —
/// <c>if (verdict == ResetVerdict.Reset) tally = 0;</c> — is the lever's entire contract with the
/// counter, restated in <see cref="Board"/>. That is deliberately not a re-implementation of any
/// arithmetic under test: every timing decision stays inside the adjudicator, and the test only
/// says what a reset costs.</para>
/// </summary>
public class BubbleResetConfirmTests
{
    /// <summary>The census the level ships with (<c>BubbleTestLayout.BubbleTarget</c>) — "hours
    /// collecting all the bubbles", in one number.</summary>
    private const int FullBoard = 112;

    /// <summary>A tally and the lever's one rule about it. Nothing else: the lever resets the
    /// board when, and only when, the adjudicator returns <see cref="ResetVerdict.Reset"/>.
    /// </summary>
    private sealed class Board
    {
        private readonly BubbleResetAdjudicator _lever = new();

        public int Tally { get; private set; } = FullBoard;

        public int Resets { get; private set; }

        public BubbleResetAdjudicator Lever => _lever;

        public ResetVerdict Pull(int byPeer, double atSec)
        {
            ResetVerdict verdict = _lever.Press(byPeer, atSec);
            if (verdict != ResetVerdict.Reset)
                return verdict;
            Tally = 0;
            Resets++;
            return verdict;
        }
    }

    // --- 1: Talon's sentence, and its companion ---------------------------------------------

    [Fact]
    public void AFullBoard_SurvivesAStrayPressFromANonConfirmingPeer()
    {
        // "I would be upset if I spent hours collecting all the bubbles and then someone reset my
        // progress." Peer 7 walks up to the lever and pulls it once, which before LEVER-1 was the
        // whole of what it took to erase everyone's night.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 7, atSec: 10.0));
        Assert.Equal(FullBoard, board.Tally);

        // They never pull again — they read the sign, or they wandered off, or they were never
        // going to. The window closes and the board is STILL THERE, which is the whole packet.
        Assert.Equal(0, board.Lever.WarningPeer(10.0 + BubbleResetConfirm.ConfirmWindowSec + 0.01));
        Assert.Equal(FullBoard, board.Tally);
        Assert.Equal(0, board.Resets);

        // And a long time later, in case anything is holding the arm open.
        Assert.Equal(FullBoard, board.Tally);
        Assert.Equal(0, board.Lever.WarningPeer(600.0));
    }

    [Fact]
    public void ADeliberateConfirmedPress_DoesReset()
    {
        // The companion, and it is not a formality: a warning that blocks the real action is a
        // different bug from the one being fixed, and it is the easier of the two to ship.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 7, atSec: 10.0));
        Assert.Equal(ResetVerdict.Reset, board.Pull(byPeer: 7, atSec: 11.0));
        Assert.Equal(0, board.Tally);
        Assert.Equal(1, board.Resets);
    }

    // --- 2: the two races the packet names ---------------------------------------------------

    [Fact]
    public void TwoPeersConfirmingAtOnce_ProduceExactlyOneReset()
    {
        // The debounce's original job, which the new gate in front of it must not have broken.
        // Both peers arm, both confirm in the same instant, and the world resets once.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 2, atSec: 0.0));
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 3, atSec: 0.02));
        Assert.Equal(ResetVerdict.Reset, board.Pull(byPeer: 2, atSec: 1.0));
        Assert.Equal(ResetVerdict.Debounced, board.Pull(byPeer: 3, atSec: 1.02));
        Assert.Equal(1, board.Resets);
        Assert.Equal(1, board.Lever.Accepted);
        Assert.Equal(1, board.Lever.Refused);
        // Both confirmations were honoured by the WARNING; only one survived the DEBOUNCE. That
        // difference is the two mechanisms staying separate, visible in the counters.
        Assert.Equal(2, board.Lever.Confirmed);
    }

    [Fact]
    public void OnePeersArm_IsNotAnotherPeersConfirm()
    {
        // The defect Talon named, in its subtlest form: two players who each pressed once, neither
        // of whom decided anything, and the board gone. Arms are per peer precisely so this cannot
        // happen — and so that the other resolution (each stealing the arm back) cannot livelock
        // the lever whenever two people stand at it.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 2, atSec: 0.0));
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 3, atSec: 0.6));
        Assert.Equal(FullBoard, board.Tally);

        // Peer 2's own arm is untouched by peer 3 having pressed, so peer 2 can still confirm.
        Assert.Equal(ResetVerdict.Reset, board.Pull(byPeer: 2, atSec: 1.2));
        Assert.Equal(1, board.Resets);
    }

    // --- 3: cancellation, both shapes --------------------------------------------------------

    [Fact]
    public void AnAbandonedConfirmation_LeavesNoStateALaterPressInherits()
    {
        // The packet's own words. The failure this forbids is the nastiest kind: a lever that
        // remembers a pull from four minutes ago and treats the next single press as a confirm.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 5, atSec: 0.0));
        double afterTheWindow = BubbleResetConfirm.ConfirmWindowSec + 0.001;
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 5, atSec: afterTheWindow));
        Assert.Equal(FullBoard, board.Tally);
        Assert.Equal(0, board.Resets);
        // And the lapsed one was counted as withdrawn, so "nothing was left behind" is an
        // observation rather than a claim.
        Assert.Equal(1, board.Lever.Cancelled);
    }

    [Fact]
    public void WalkingAway_WithdrawsTheWarning()
    {
        // The active half of "it must be cancellable". InteractHighlighter clears Highlighted the
        // moment the avatar leaves PressRadius, and the lever turns that already-measured gesture
        // into this call — no second key, no new input path.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 4, atSec: 0.0));
        Assert.Equal(4, board.Lever.WarningPeer(0.1));
        Assert.True(board.Lever.Cancel(4));
        Assert.Equal(0, board.Lever.WarningPeer(0.2));

        // Having backed out, the next press arms again rather than confirming.
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 4, atSec: 0.3));
        Assert.Equal(FullBoard, board.Tally);
    }

    [Fact]
    public void CancellingWithNothingPending_IsANoOpAndNotAnError()
    {
        // The highlighter clears on every walk-away, armed or not, so this path is taken
        // constantly. It must be free and it must not invent a cancellation.
        var confirm = new BubbleResetConfirm();
        Assert.False(confirm.Cancel(9));
        Assert.Equal(0, confirm.Cancelled);
    }

    // --- 4: the warning cannot be defeated by mashing -----------------------------------------

    [Fact]
    public void ADoubleTap_CannotDefeatTheWarning()
    {
        // Without the minimum dwell, two presses in the same tenth of a second would arm and
        // confirm, and a player who never looked at the sign would wipe the board exactly as
        // before — the warning reduced to a tax on the people already being careful.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 1, atSec: 0.0));
        for (int i = 1; i <= 6; i++)
            Assert.Equal(ResetVerdict.Ignored, board.Pull(byPeer: 1, atSec: i * 0.07));
        Assert.Equal(FullBoard, board.Tally);
        Assert.Equal(6, board.Lever.Ignored);

        // The warning is still standing after the mash — an ignored press must not consume it,
        // or a jittery keyboard would silently disarm a player mid-decision.
        Assert.Equal(1, board.Lever.WarningPeer(0.5));
        Assert.Equal(ResetVerdict.Reset, board.Pull(byPeer: 1, atSec: 1.0));
    }

    [Fact]
    public void TheDwellAndTheWindow_BracketTheConfirm()
    {
        // Both boundaries, stated rather than left to fall out (MECHANICS-BIBLE §1). The dwell is
        // EXCLUSIVE — a press at exactly MinDwellSec confirms; the window is INCLUSIVE — a press
        // at exactly ConfirmWindowSec confirms and one a hair past it does not.
        Assert.Equal(ResetVerdict.Reset, PressPair(BubbleResetConfirm.MinDwellSec));
        Assert.Equal(ResetVerdict.Ignored, PressPair(BubbleResetConfirm.MinDwellSec - 0.001));
        Assert.Equal(ResetVerdict.Reset, PressPair(BubbleResetConfirm.ConfirmWindowSec));
        Assert.Equal(ResetVerdict.Armed, PressPair(BubbleResetConfirm.ConfirmWindowSec + 0.001));
    }

    private static ResetVerdict PressPair(double gapSec)
    {
        var lever = new BubbleResetAdjudicator();
        lever.Press(1, 100.0);
        return lever.Press(1, 100.0 + gapSec);
    }

    [Fact]
    public void TheDwellIsShorterThanTheWindow_OrTheLeverIsUnusable()
    {
        // A dwell at or past the window would leave no instant at which a confirm is accepted, and
        // the failure would look like "the lever is broken", not like a bad constant.
        Assert.True(BubbleResetConfirm.MinDwellSec < BubbleResetConfirm.ConfirmWindowSec,
            $"dwell {BubbleResetConfirm.MinDwellSec}s must be inside the "
            + $"{BubbleResetConfirm.ConfirmWindowSec}s window");
    }

    // --- 5: the two timers are genuinely separate ---------------------------------------------

    [Fact]
    public void AnArmingPress_DoesNotSpendTheDebounce()
    {
        // The ordering bug this whole class of test exists to catch: if the first press went
        // through BubbleResetGate, the confirming press inside the 1.2 s cooldown would be refused
        // and the lever could never be used at all. Both rule classes stay individually correct
        // under that bug; only the composition catches it.
        var board = new Board();
        Assert.Equal(ResetVerdict.Armed, board.Pull(byPeer: 1, atSec: 0.0));
        Assert.True(BubbleResetConfirm.MinDwellSec < BubbleResetGate.CooldownSec,
            "this test is only meaningful while the confirm can land inside the cooldown");
        Assert.Equal(ResetVerdict.Reset, board.Pull(byPeer: 1, atSec: 0.9));
        Assert.Equal(0, board.Tally);
        Assert.Equal(0, board.Lever.Refused);
    }

    [Fact]
    public void TheWarningAndTheDebounce_AreDistinctDurations()
    {
        // Not a style assertion. If the two constants were ever collapsed into one, the argument
        // for keeping them apart — one must be short enough to catch a double-tap, the other long
        // enough to read a sign — would have been quietly lost, and this is what would say so.
        Assert.NotEqual(BubbleResetConfirm.ConfirmWindowSec, BubbleResetGate.CooldownSec);
        Assert.True(BubbleResetConfirm.ConfirmWindowSec > BubbleResetGate.CooldownSec,
            "the window must outlast the debounce, or a confirmed reset could be refused and the "
            + "warning it consumed would be gone with nothing to show for it");
    }

    // --- 6: the sign the warning is displayed on ----------------------------------------------

    [Fact]
    public void OnlyOneWarningIsDisplayed_AndItIsTheNewest()
    {
        // The lever has one sign and six peers may arm it. Showing the newest is the only choice
        // that keeps the plate honest as arms come and go; "whichever the dictionary yields" is
        // the non-deterministic answer MECHANICS-BIBLE §3 is about.
        var confirm = new BubbleResetConfirm();
        confirm.Press(2, 0.0);
        confirm.Press(3, 0.5);
        confirm.Press(4, 1.0);
        Assert.Equal(4, confirm.NewestArmed(1.1));

        // 4 walks away; the sign falls back to 3, which is still standing — not to quiet, and not
        // to 2, which armed first.
        confirm.Cancel(4);
        Assert.Equal(3, confirm.NewestArmed(1.2));
    }

    [Fact]
    public void WhenEveryWarningHasLapsed_TheSignGoesQuiet()
    {
        var confirm = new BubbleResetConfirm();
        confirm.Press(2, 0.0);
        confirm.Press(3, 0.5);
        Assert.Equal(3, confirm.NewestArmed(1.0));
        Assert.Equal(0, confirm.NewestArmed(0.5 + BubbleResetConfirm.ConfirmWindowSec + 0.001));
        Assert.Equal(2, confirm.Cancelled);
    }

    [Fact]
    public void RemainingSec_CountsDownAndNeverGoesNegative()
    {
        var confirm = new BubbleResetConfirm();
        Assert.Equal(0, confirm.RemainingSec(1, 0));      // never armed
        confirm.Press(1, 4.0);
        Assert.Equal(BubbleResetConfirm.ConfirmWindowSec, confirm.RemainingSec(1, 4.0), 6);
        Assert.Equal(BubbleResetConfirm.ConfirmWindowSec / 2,
            confirm.RemainingSec(1, 4.0 + BubbleResetConfirm.ConfirmWindowSec / 2), 6);
        Assert.Equal(0, confirm.RemainingSec(1, 4.0 + BubbleResetConfirm.ConfirmWindowSec));
        Assert.Equal(0, confirm.RemainingSec(1, 4000.0));
        Assert.Equal(0, confirm.RemainingSec(99, 4.0));   // a peer that never pressed
    }

    [Fact]
    public void ArmsDoNotAccumulate_AcrossALongSession()
    {
        // Six peers, an hour of idle pulls nobody follows through on. Lapsed arms are pruned, so
        // the dictionary cannot grow with session length — the quiet leak that only shows up in
        // the one session long enough to matter.
        var confirm = new BubbleResetConfirm();
        for (int i = 0; i < 2000; i++)
            confirm.Press(1 + (i % 6), i * 10.0);
        Assert.False(confirm.AnyArmed(20_000.0));
        Assert.Equal(2000, confirm.Armed);
        Assert.Equal(0, confirm.Confirmed);
    }

    // --- 7: the sign itself, and the promise it must not break --------------------------------

    [Fact]
    public void TheAuthoredSign_SaysWhatTheCodeThinksItSays()
    {
        // Two copies of the idle text exist by design — the scene's, which the editor and a packed
        // load see, and BubbleResetLever.SignIdleText, which the runtime writes. This is what stops
        // them drifting apart, which would show up as a sign that says one thing until the first
        // time anything touches it.
        string scene = ReadRepoFile("scenes/game/props/BubbleResetLever.tscn");
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        string authored = ReadConst(lever, "SignIdleText");
        Assert.Equal(4, CountOccurrences(scene, "text = \"" + authored + "\""));
    }

    [Fact]
    public void TheSignSaysWhatTheLeverDoes_AndNamesTheLoss()
    {
        // Talon asked for "a sign at least that it will reset the bubble counter". A sign reading
        // only "RESET" would satisfy the letter and not the sentence — the reason he was going to
        // be upset is the losing, so the sign names the losing.
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        string idle = ReadConst(lever, "SignIdleText");
        Assert.Contains("BUBBLE", idle);
        Assert.Contains("ZERO", idle);
        string armed = ReadConst(lever, "SignArmedFormat");
        Assert.Contains("{0}", armed);      // the live tally at risk, not a generic caution
        Assert.Contains("AGAIN", armed);    // and what to do about it
    }

    [Fact]
    public void TheSign_DoesNotBillboard()
    {
        // UI-1 removed five camera-facing SectionLabels from this level on 2026-08-28 because
        // Talon objected to billboarded hover UI. Adding one back in the same week, in the same
        // level, would be undoing his note with his other note.
        string scene = ReadRepoFile("scenes/game/props/BubbleResetLever.tscn");
        Assert.Equal(4, CountOccurrences(scene, "type=\"Label3D\""));
        foreach (string enabled in new[] { "billboard = 1", "billboard = 2", "billboard = 3" })
            Assert.DoesNotContain(enabled, scene);
    }

    [Fact]
    public void TheBillboardControl_WouldActuallyFail()
    {
        // The positive control the test above is worth nothing without: the matcher has to be able
        // to SEE an enabled billboard when one is there. The green hills section authors one.
        string green = ReadRepoFile("scenes/game/world/bubbletest/sections/GreenHills.tscn");
        Assert.Contains("billboard = 1", green);
    }

    [Fact]
    public void TheSign_AddsNoLight_AndNothingHereIsEverAModal()
    {
        // Program D10 — "nothing else in the level gets a light" — and UI-1's direction, both of
        // which a sign is the obvious way to break. Label3D is unshaded, so it stays readable at
        // night without lighting anything.
        string scene = ReadRepoFile("scenes/game/props/BubbleResetLever.tscn");
        Assert.DoesNotContain("Light3D", scene);
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        Assert.DoesNotContain("AcceptDialog", lever);
        Assert.DoesNotContain("ConfirmationDialog", lever);

        // THE "NO SCREEN-SPACE UI" HALF OF THIS TEST WAS REPEALED BY TALON ON THE SAME DAY IT
        // SHIPPED, and that is recorded here rather than quietly deleted, because a reader who
        // finds a screen-space warning on this lever should find out why in one read. LEVER-1
        // asserted the lever built no CanvasLayer, on UI-1's direction against hovering world UI.
        // Talon's second-pass note 6 then asked for exactly one: "please include a non-diegetic
        // 'pop up' ... because the warning is good but it's too small for them to read and usually
        // the player character is blocking the text anyway." UI-1's direction was against
        // BILLBOARDED WORLD UI, which TheSign_DoesNotBillboard above still enforces; a plate in
        // screen space is the opposite kind of thing, and the only kind a body cannot stand in
        // front of.
        //
        // What survives from the old assertion is the part that was always the point: the warning
        // must not become a dialog. It takes no input, steals no focus, and cannot be entered or
        // left, so there is no state a player can get stuck in.
        string popup = ReadRepoFile("scripts/ui/ConsequenceWarning.cs");
        Assert.DoesNotContain("AcceptDialog", popup);
        Assert.DoesNotContain("ConfirmationDialog", popup);
        Assert.DoesNotContain("GrabFocus", popup);
        Assert.DoesNotContain("SetProcessInput", popup);
        Assert.DoesNotContain("_UnhandledInput", popup);
        Assert.DoesNotContain("MouseFilterEnum.Stop", popup);
        Assert.DoesNotContain("MouseFilterEnum.Pass", popup);
        Assert.Contains("MouseFilterEnum.Ignore", popup);
    }

    // --- LEVER-2: the non-diegetic pop-up -------------------------------------------------------

    [Fact]
    public void TheResetIsShared_NotPersonal_WhichIsWhatTheCopyHasToSay()
    {
        // THE EVIDENCE, not an assumption. The packet: "If the reset is shared across all players
        // rather than personal — check, do not assume — then the copy has to say so."
        //
        // BubbleCounterState is the whole of what a reset destroys, and its public surface cannot
        // express "whose": nothing on it takes or returns a peer id, so there is no per-player
        // popped set to reset and no way to add one without this test noticing. That is the
        // structural half of the proof; the behavioural half follows it.
        foreach (System.Reflection.MethodInfo m in typeof(BubbleCounterState)
                     .GetMethods(System.Reflection.BindingFlags.Public
                                 | System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.DeclaredOnly))
        {
            foreach (System.Reflection.ParameterInfo p in m.GetParameters())
                Assert.DoesNotContain("peer", p.Name!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("peer", m.Name, StringComparison.OrdinalIgnoreCase);
        }

        // One bitset, one count, one Reset() that empties it. A pop made by "somebody else" is
        // indistinguishable from one you made, because the state never recorded the difference —
        // so a reset can only ever cost everybody everything.
        var state = new BubbleCounterState();
        Assert.True(state.TryPop(3));
        Assert.True(state.TryPop(41));
        Assert.Equal(2, state.Count);
        state.Reset();
        Assert.Equal(0, state.Count);
        Assert.False(state.IsPopped(3));
        Assert.False(state.IsPopped(41));
    }

    [Fact]
    public void ThePopupCopy_SaysSharedAndSaysEveryone()
    {
        // The whole reason the pop-up exists is that a player has to be able to refuse. "This will
        // reset your bubbles" would be a strictly smaller warning than the truth measured above,
        // and Talon's original complaint was about losing somebody ELSE's hours.
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");

        string idle = ReadConst(lever, "PopupIdleBodyFormat");
        Assert.Contains("{0}", idle);           // a number is refusable; "the counter" is not
        Assert.Contains("SHARED", idle);
        Assert.Contains("EVERYONE", idle);

        string armedSelf = ReadConst(lever, "PopupArmedSelfHeadingFormat");
        Assert.Contains("{0}", armedSelf);
        Assert.Contains("AGAIN", armedSelf);    // two-stage press: what to do, or not do, next

        string armedOther = ReadConst(lever, "PopupArmedOtherHeadingFormat");
        Assert.Contains("{0}", armedOther);
        Assert.Contains("RISK", armedOther);

        // The empty-board wordings, which the LEVER-2 capture found the hard way: the shipped
        // frame said "puts all 0 bubbles back" and "0 BUBBLES AT RISK". Both are sentences that
        // read as a bug, on the one screen whose whole job is to be believed. They carry the same
        // rule with no tally in them, so a number never appears where there is nothing to count.
        string idleEmpty = ReadConst(lever, "PopupIdleBodyEmpty");
        Assert.DoesNotContain("{0}", idleEmpty);
        Assert.Contains("SHARED", idleEmpty);
        Assert.Contains("EVERYONE", idleEmpty);
        string armedEmpty = ReadConst(lever, "PopupArmedEmptyHeading");
        Assert.DoesNotContain("{0}", armedEmpty);
        Assert.DoesNotContain("0 BUBBLES", armedEmpty);

        // Nowhere may the copy call the bubbles the reader's own. This is the assertion that fails
        // if a later edit softens the sentence back to "your progress".
        foreach (string name in new[]
                 {
                     "PopupIdleBodyFormat", "PopupIdleBodyEmpty", "PopupIdleFootFormat",
                     "PopupArmedSelfHeadingFormat", "PopupArmedSelfBody", "PopupArmedSelfFootFormat",
                     "PopupArmedOtherHeadingFormat", "PopupArmedOtherBody", "PopupArmedOtherFootFormat",
                     "PopupArmedEmptyHeading",
                 })
        {
            string copy = ReadConst(lever, name);
            Assert.DoesNotContain("your bubbles", copy, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("your progress", copy, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("your count", copy, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// <b>Both lever prompts say which key does it, and neither hardcodes the letter.</b>
    ///
    /// <para>Talon, 2026-08-30 playtest note 3, verbatim: <i>"On the bubble reset lever, when this
    /// pops up, please include in the text 'press E to reset' and then when the warning is shown
    /// again, again include 'press E to reset' to make it clear to the users what to do."</i> He
    /// said it twice on purpose — the plate that appears when you walk up, and the plate that
    /// replaces it once the lever is armed — so both are asserted, in all three armed wordings.</para>
    ///
    /// <para><b>And the letter itself must NOT be in the string.</b> His note names E because E is
    /// what is bound; a plate that prints a literal "E" is correct today and wrong the first time
    /// anything rebinds interact or a non-QWERTY layout resolves it differently. The copy carries
    /// <c>{0}</c> and <c>BubbleResetLever.InteractKeyLabel</c> fills it from the live InputMap,
    /// which is the same resolver HOW TO PLAY and the world-space interact chip already use.</para>
    /// </summary>
    [Fact]
    public void ThePopupCopy_NamesTheKeyAndDoesNotHardcodeIt()
    {
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");

        foreach (string name in new[]
                 { "PopupIdleFootFormat", "PopupArmedSelfFootFormat", "PopupArmedOtherFootFormat" })
        {
            string copy = ReadConst(lever, name);
            Assert.Contains("{0}", copy);
            Assert.Contains("reset", copy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Press {0}", copy, StringComparison.OrdinalIgnoreCase);
            // The defect the {0} exists to prevent: a bare key letter baked into the sentence.
            Assert.DoesNotMatch(@"[Pp]ress ['""]?E\b", copy);
        }

        // The binding is resolved, not assumed — through the repo's one resolver rather than a
        // fourth private copy of the same InputMap loop.
        Assert.Contains("ControlGlyphs.BindingFor(Action)", lever, StringComparison.Ordinal);
        Assert.Contains("InputMap.HasAction(Action)", lever, StringComparison.Ordinal);
    }

    /// <summary>Positive control for the hardcoded-letter matcher above: it has to be able to see
    /// the sentence Talon literally asked for before "no string contains it" means anything.</summary>
    [Fact]
    public void TheHardcodedKeySweep_WouldActuallyFail()
    {
        const string Planted = "One pull arms it; press E to reset.";
        Assert.Matches(@"[Pp]ress ['\""]?E\b", Planted);
    }

    [Fact]
    public void ThePopupCopySweep_WouldActuallyFail()
    {
        // The positive control for the sweep above: prove the matcher can see the phrase it hunts,
        // or "no string says 'your bubbles'" is a claim about a broken scanner.
        const string Planted = "Pulling this resets YOUR BUBBLES.";
        Assert.Contains("your bubbles", Planted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePopupCannotBeOccluded_AndCannotCoverTheAimPoint()
    {
        // The literal complaint was "the player character is blocking the text". A CanvasLayer
        // composites over the 3D viewport, so nothing in the world can stand in front of it —
        // THAT property is the fix, and it is why a bigger Label3D is not one.
        string popup = ReadRepoFile("scripts/ui/ConsequenceWarning.cs");
        Assert.Contains(": CanvasLayer", popup);
        Assert.Contains("Layer = UiLayers.ConsequenceWarning", popup);

        // And the standing law it plays under: it draws while the player still has the camera and
        // the stick, so it hangs off the lower-band FRACTION rather than a pixel lift and cannot
        // cross the aim point at any frame height (UiCoverageLaw).
        Assert.Contains("UiColumns.LowerBandTopAnchor", popup);
        Assert.True(MpFoundation.Ui.Design.UiColumns.LowerBandTopAnchor > 0.5f,
            "the plate's anchor is at or above the middle of the frame — it can cover the aim point");

        // And it leaves on its own, off the same proximity flag that shimmers the lever, so there
        // is nothing to dismiss and nothing to linger.
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        Assert.Contains("RefreshPopup();", lever);
        Assert.Contains("_popup.Clear();", lever);
    }

    [Fact]
    public void ThePopup_TypesNoColourOfItsOwn()
    {
        // UI-3 landed the moonlight accent and confirmed by grep that no UI screen types its own
        // colour. UiNoBespokeStylingTests sweeps scripts/ui/** for exactly that and would catch a
        // hex here; this is the narrow, named version, so a failure points at this packet rather
        // than at a project-wide sweep. Every colour the plate paints comes from UiTokens through
        // the theme or through UiThemeService.Tokens.
        string popup = ReadRepoFile("scripts/ui/ConsequenceWarning.cs");
        Assert.DoesNotContain("Color(\"", popup);
        Assert.Contains("UiThemeService.Tokens", popup);
        Assert.Contains("UiThemeService.Bind", popup);   // and follows the palette across dusk
    }

    [Fact]
    public void TheSignIsLegible_AtTheDistanceThePlayerStandsToPressIt()
    {
        // The packet: "legible at the distance a player actually stands to press the lever.
        // Measure it; do not assume." Measured from the authored numbers here, and confirmed
        // against a render in the LEVER-1 report.
        //
        // A Label3D's em height in metres is font_size x pixel_size, and a capital is ~0.70 em.
        // The comfortable-reading threshold is ~20 arcmin of subtended angle (0.0058 rad); the
        // acuity limit is ~5 arcmin. PressRadius is the WORST case: a player any further away
        // cannot press it at all.
        string scene = ReadRepoFile("scenes/game/props/BubbleResetLever.tscn");
        double em = ReadNumber(scene, "font_size = ") * ReadNumber(scene, "pixel_size = ");
        double capM = 0.70 * em;
        double subtendedRad = capM / BubbleResetLeverPressRadiusM;
        Assert.True(subtendedRad > 0.0058,
            $"a {capM * 1000:F1} mm capital subtends {subtendedRad * 3437.7:F1} arcmin at "
            + $"{BubbleResetLeverPressRadiusM} m — under the ~20 arcmin that reads comfortably");

        // And the other end: the longest line has to fit the narrow (0.5 m) face it is mounted on.
        // ~0.6 em per uppercase character is the conservative estimate for the project's sans.
        const double NarrowFaceM = 0.5;
        int longest = 0;
        foreach (string line in ReadConst(
                     ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs"), "SignIdleText")
                     .Split(new[] { "\\n" }, StringSplitOptions.None))
            longest = Math.Max(longest, line.Length);
        double widthM = longest * 0.6 * em;
        Assert.True(widthM < NarrowFaceM,
            $"the longest line ({longest} chars, ~{widthM:F2} m) overhangs the {NarrowFaceM} m face");
    }

    /// <summary>The lever's own press radius, restated so this test file does not need Godot to
    /// read a const off a <c>Node3D</c>. Pinned against the source below.</summary>
    private const double BubbleResetLeverPressRadiusM = 1.6;

    [Fact]
    public void ThePressRadiusThisFileMeasuresAgainst_IsTheLeversOwn()
    {
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        Assert.Equal(BubbleResetLeverPressRadiusM, ReadNumber(lever, "PressRadius = "), 6);
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>First numeric literal after <paramref name="marker"/>, trailing <c>f</c> and
    /// <c>;</c> tolerated.</summary>
    private static double ReadNumber(string source, string marker)
    {
        int at = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"marker not found: {marker}");
        int i = at + marker.Length;
        var digits = new System.Text.StringBuilder();
        while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.' || source[i] == '-'))
            digits.Append(source[i++]);
        return double.Parse(digits.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The literal of a <c>public const string Name = "…";</c>, exactly as written in
    /// the source — escape sequences left escaped, which is what the <c>.tscn</c> holds too.
    ///
    /// <para>Reads to the terminating semicolon and concatenates every string literal it passes,
    /// so a const written across several lines with <c>+</c> reads back as the one sentence it
    /// compiles to. LEVER-2 needed that: the pop-up's copy does not fit on a line, and the first
    /// cut of this helper — which required the opening quote to sit immediately after
    /// <c>Name = </c> — would have thrown "const not found" on a const that is plainly
    /// there.</para></summary>
    private static string ReadConst(string source, string name)
    {
        string marker = name + " =";
        int at = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"const not found: {name}");
        int i = at + marker.Length;
        var text = new System.Text.StringBuilder();
        bool sawLiteral = false;
        while (i < source.Length && source[i] != ';')
        {
            if (source[i] != '"')
            {
                i++;
                continue;
            }
            sawLiteral = true;
            i++;                                            // past the opening quote
            while (i < source.Length && !(source[i] == '"' && source[i - 1] != '\\'))
                text.Append(source[i++]);
            i++;                                            // past the closing quote
        }
        Assert.True(sawLiteral, $"const {name} has no string literal before its semicolon");
        return text.ToString();
    }

    [Fact]
    public void TheConstReader_HandlesBothSpellingsItIsPointedAt()
    {
        // The positive control for the helper above, because every copy assertion in this file is
        // only as trustworthy as its reader. One-line and split-across-lines must come back the
        // same, and a name that is not there must fail rather than return empty.
        const string OneLine = "    public const string A = \"RESET\\nALL BACK\";\n";
        const string Split = "    public const string B =\n        \"first half \"\n        + \"second half\";\n";
        Assert.Equal("RESET\\nALL BACK", ReadConst(OneLine, "A"));
        Assert.Equal("first half second half", ReadConst(Split, "B"));
        Assert.ThrowsAny<Exception>(() => ReadConst(OneLine, "NotDeclaredAnywhere"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
