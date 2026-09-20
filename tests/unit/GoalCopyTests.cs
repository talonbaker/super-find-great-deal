using System;
using System.Linq;
using MpFoundation.Game.World;
using MpFoundation.Ui;
using MpFoundation.Ui.Hud;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The level has a goal, and every surface that mentions one has to say the same thing.</b>
///
/// <para><b>Why this file exists</b> (2026-09-04, Talon's direction). Until this change the build
/// carried the opposite claim in three places at once: HOW TO PLAY opened with <i>"There is no
/// score, no objective and no way to lose"</i>, and two more surfaces documented themselves around
/// the same 2026-08-29 note. Copy that denies a mechanic the world HAS is the same defect as copy
/// asserting one it lacks — note 4, pointed the other way — and it is exactly the kind of thing
/// that rots silently, because no test has ever read a sentence. These do.</para>
///
/// <para>The same change removed the first-run How-to-Play door and replaced it with a permanent
/// HUD hint, so the last group here reads that hint's sentence too: it is the other half of what
/// the build now says to a player walking in, and it can go wrong in exactly the same way — by
/// naming a screen the panel is not on.</para>
///
/// <para><b>Amended later the same day (COPY-1).</b> Talon then wrote HOW TO PLAY's prose himself,
/// and his three lines neither restate the objective nor keep the phrase "no way to lose". Two
/// assertions here were written against copy an agent had authored and became assertions against
/// HIS copy the moment it landed; both were rewritten rather than deleted, each carrying why. The
/// objective is unchanged — it is still announced on entry by the toast, and the first two tests
/// below still hold it. What no longer holds is the COUPLING between the toast and the screen.
/// <c>ForewordCopyTests</c> is where Talon's own words are pinned verbatim.</para>
/// </summary>
public class GoalCopyTests
{
    [Fact]
    public void TheGoalLine_SaysWhatToDo()
    {
        Assert.False(string.IsNullOrWhiteSpace(PhaseToastText.BubbleGoalToast));
        Assert.Contains("bubble", PhaseToastText.BubbleGoalToast, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>It is raised on entry, by hand, not by a crossing — so no
    /// <see cref="PhaseEventKind"/> may ever resolve to it. If one did, the goal would re-announce
    /// itself at sunset.</summary>
    [Fact]
    public void TheGoalLine_IsNotReachableFromACrossing()
    {
        foreach (PhaseEventKind kind in Enum.GetValues<PhaseEventKind>())
            Assert.NotEqual(PhaseToastText.BubbleGoalToast, PhaseToastText.TextFor(kind));
    }

    /// <summary>
    /// <b>RETIRED 2026-09-04 (COPY-1), by Talon's own copy.</b> This used to assert that HOW TO
    /// PLAY opened with the toast's sentence verbatim — the two surfaces pinned to each other so a
    /// reword of one that forgot the other went red.
    ///
    /// <para>That pin was correct while both surfaces were written by agents. It stopped being
    /// correct when Talon wrote HOW TO PLAY himself: his three GOOD TO KNOW lines are about
    /// running, drowning and salt, and none of them restates the objective. He replaced the screen,
    /// not only its wording. Per <c>CLAUDE.md</c> — when his direction and a document disagree, the
    /// document is what is wrong — the assertion is what gave, not the copy.</para>
    ///
    /// <para><b>What survives.</b> The objective itself is unchanged and still announced on entry
    /// by <see cref="PhaseToastText.BubbleGoalToast"/>; the first two tests in this file still hold
    /// it, and <c>ForewordCopyTests</c> now holds Talon's three lines character-for-character. What
    /// is gone is only the coupling BETWEEN the two surfaces, which is a thing he is allowed to
    /// break. All that is asserted here now is that the screen still says something.</para>
    /// </summary>
    [Fact]
    public void HowToPlay_StillSaysSomething()
    {
        Assert.NotEmpty(HowToPlayContent.Lines);
        Assert.All(HowToPlayContent.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    /// <summary>The claim that was actually on screen. Asserted as a negative over the whole list
    /// rather than over line 0, because the denial could be re-added anywhere in it.</summary>
    [Fact]
    public void NoShippedLine_StillDeniesTheObjective()
    {
        foreach (string line in HowToPlayContent.Lines)
        {
            Assert.DoesNotContain("no objective", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no score", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// <b>RETIRED 2026-09-04 (COPY-1), by Talon's own copy.</b> This used to require the phrase
    /// "no way to lose" somewhere in HOW TO PLAY, so that a rewrite could not quietly invent a fail
    /// state the build does not have.
    ///
    /// <para>Talon's replacement lines say the opposite in a comic register — water "will kill
    /// you", the bubbles "will also kill you" — and they are shipping verbatim. Kept as a rewritten
    /// assertion rather than deleted, because the fact underneath is still true and still worth
    /// holding: <b>nothing on this screen may promise a real loss condition</b>. Death here is a
    /// respawn (<c>RespawnCause.Drowned</c>), not a defeat, and the words that would signal an
    /// actual fail state — a game over, a run lost, a score — are the ones to keep out.</para>
    ///
    /// <para>"Kill" is deliberately not in that list. It is in Talon's copy, it is accurate about
    /// drowning, and it does not claim the run can end.</para>
    /// </summary>
    [Fact]
    public void NoShippedLine_InventsAFailState()
    {
        foreach (string line in HowToPlayContent.Lines)
        {
            Assert.DoesNotContain("game over", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("you lose", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lose the run", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("start over", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- the permanent hint that replaced the first-run door -------------------------------------

    /// <summary>The authored line, on the shipped binding. Asserted through the format function
    /// rather than the live property because <c>InputMap</c> needs an engine and the sentence does
    /// not — and the sentence is the part that can be wrong.</summary>
    [Fact]
    public void TheHint_ReadsAsAuthored()
    {
        Assert.Equal("ESC · HOW TO PLAY", HudHowToPlayHint.Format("Esc"));
    }

    /// <summary>The key is upper-cased into the HUD's register wherever the binding table's own
    /// casing lands — <c>ControlGlyphs</c> shortens "Escape" to the sentence-cased "Esc" for the
    /// How-to-Play panel's caps, and this hint is not that surface.</summary>
    [Theory]
    [InlineData("Esc")]
    [InlineData("esc")]
    [InlineData("ESC")]
    public void TheHint_UpperCasesWhateverTheBindingTableGivesIt(string label)
    {
        Assert.StartsWith("ESC ", HudHowToPlayHint.Format(label), StringComparison.Ordinal);
    }

    /// <summary><b>HOW TO PLAY is in the pause menu, not in Settings.</b> The one way this line can
    /// actively mislead is by naming the wrong screen: a player sent to Settings finds display and
    /// audio and no controls, which is worse than no hint at all. It is also the likeliest reword,
    /// because "Settings" is where a hint like this lives in most games.</summary>
    [Fact]
    public void TheHint_DoesNotSendThePlayerToSettings()
    {
        Assert.DoesNotContain("setting", HudHowToPlayHint.Format("Esc"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOW TO PLAY", HudHowToPlayHint.Format("Esc"), StringComparison.Ordinal);
    }

    /// <summary>The hint advertises the same action the panel closes on, so the key it prints and
    /// the key that works are one binding rather than two that agree today.</summary>
    [Fact]
    public void TheHint_NamesThePauseAction()
    {
        Assert.Equal("pause", HudHowToPlayHint.PauseAction);
    }
}
