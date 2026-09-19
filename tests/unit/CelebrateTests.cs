using System;
using MpFoundation.Game.Sandbox;
using MpFoundation.Ui;
using Sail.Game.Achievements;
using Sail.Game.Bubble;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>CELEBRATE-1 (2026-09-04): the level notices when the last bubble goes.</b>
///
/// <para>Three separable things, and this file covers exactly the parts of them that are pure —
/// the same boundary <c>HonkTests</c> draws between <c>HonkGate</c> (here) and
/// <c>HonkManager</c> (a live server):</para>
/// <list type="number">
/// <item><b>The gate</b> — <see cref="BubbleCelebration.ShouldCelebrate"/>. A bot must not
/// celebrate, and the positive control that a person still does. The ENGINE half of that gate (a
/// scripted intent source really produces a body that answers false) is measured in
/// <c>SandboxSelfTest.RunAchievementGateTestsAsync</c>, beside the achievement gate it shares a
/// seam with, and end-to-end in <c>tests/Run-CelebrateTest.ps1</c>.</item>
/// <item><b>The completion rule</b> — which is <see cref="BubblholicRule.AllPopped"/>, reused
/// rather than respelled, so the sound and the achievement can never disagree about what
/// completing the level means. Pinned here as an identity claim, because the reuse is the
/// property.</item>
/// <item><b>The sound</b> — <see cref="SfxLab.TriumphPcm"/>, raw float PCM built out of pure
/// <c>Mathf</c> maths and therefore reachable without an audio device. What is asserted is exactly
/// the set of properties <c>DECISION-LOG.md</c> claims and no more: Talon owns the taste, this
/// owns the facts that would make the taste unhearable.</item>
/// </list>
/// </summary>
public class CelebrateTests
{
    // --- The bot gate ---------------------------------------------------------------------------

    /// <summary>
    /// <b>A scripted body celebrates nothing.</b> The rule from <c>.claude/rules/test-suite.md</c>
    /// this exists for: bubble suites pop bubbles, a suite that pops them ALL would fire the
    /// triumph on every automated run, and the counter is shared — so a bot completing the level
    /// would celebrate at a real player standing next to it.
    ///
    /// <para><b>Carries its own positive control on the next line</b>, because a gate wired to
    /// <c>false</c> satisfies the absence check alone and would be indistinguishable from a
    /// working one.</para>
    /// </summary>
    [Fact]
    public void TheGateRefusesAPeerWithNobodyDrivingABody()
    {
        Assert.False(BubbleCelebration.ShouldCelebrate(humanPresent: false, forced: false));
        Assert.True(BubbleCelebration.ShouldCelebrate(humanPresent: true, forced: false));
    }

    /// <summary><c>--celebrate-force</c> is the suite's override and it must be the ONLY thing that
    /// can open the gate for a peer nobody is driving. Pinned so a future edit that reads some
    /// other flag into the decision shows up here rather than in a playtest.</summary>
    [Fact]
    public void OnlyTheTestOverrideOpensTheGateForABot()
    {
        Assert.True(BubbleCelebration.ShouldCelebrate(humanPresent: false, forced: true));
        Assert.True(BubbleCelebration.ShouldCelebrate(humanPresent: true, forced: true));
    }

    /// <summary>
    /// <b>The gate's INPUT is the same interface the achievement gate reads.</b>
    /// <c>SandboxAvatar.IsHumanDriven</c> is <c>IIntentSource.IsHumanInput</c> and nothing else,
    /// and the interface default is "not a person" — so a scripted source added tomorrow is
    /// excluded without anyone remembering to exclude it.
    ///
    /// <para>Duplicated in spirit with <c>BotAchievementGateTests.TheInterfaceDefaultIsNotHuman</c>
    /// on purpose: that test guards the achievement writer, this one guards the celebration, and
    /// the day someone "simplifies" the interface both should go red rather than one.</para>
    /// </summary>
    [Fact]
    public void TheGateReadsTheSameHumanInputSeamTheAchievementDoes()
    {
        Assert.False(((IIntentSource)new ScriptedDouble()).IsHumanInput);
        Assert.True(((IIntentSource)new HumanDouble()).IsHumanInput);
    }

    private sealed class ScriptedDouble : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
    }

    private sealed class HumanDouble : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
        public bool IsHumanInput => true;
    }

    // --- The completion rule --------------------------------------------------------------------

    /// <summary>
    /// <b>An empty level has not been completed.</b> The guard that matters most in practice: the
    /// CI scaffolding worlds carry no bubbles at all, and a bare <c>count &gt;= total</c> is TRUE
    /// at 0 of 0 — every such session would announce a completed level on its first frame and
    /// every peer would hear a triumph for a level with nothing in it.
    /// </summary>
    [Fact]
    public void AWorldWithNoBubblesIsNeverComplete()
    {
        Assert.False(BubblholicRule.AllPopped(0, 0));
    }

    /// <summary>The bounds either side of completion, pinned rather than inherited
    /// (MECHANICS-BIBLE §1). One short is not complete; exactly all is; and a count that has
    /// somehow overshot still is, because refusing there would mean a divergence silently
    /// swallowing the whole event.</summary>
    [Theory]
    [InlineData(99, 100, false)]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, true)]
    [InlineData(0, 100, false)]
    [InlineData(5, 6, false)]
    [InlineData(6, 6, true)]
    public void CompletionIsInclusiveAtTheTotal(int count, int total, bool complete)
    {
        Assert.Equal(complete, BubblholicRule.AllPopped(count, total));
    }

    // --- The line -------------------------------------------------------------------------------

    /// <summary>
    /// <b>The completion line names no bubble count.</b> It would be a fourth copy of a number
    /// that already lives in the level's authoring, in <c>BubbleTestLayout</c> and on the HUD the
    /// player is looking at when it fires — and a packet that changes the count would have to
    /// remember to change a string in the UI layer. The failure this catches is a well-meaning
    /// edit to "100 / 100!".
    /// </summary>
    [Fact]
    public void TheCompletionLineCarriesNoNumber()
    {
        foreach (char c in PhaseToastText.AllBubblesToast)
            Assert.False(char.IsDigit(c), $"'{PhaseToastText.AllBubblesToast}' names a number");
        Assert.DoesNotContain("!", PhaseToastText.AllBubblesToast, StringComparison.Ordinal);
        Assert.NotEqual(PhaseToastText.BubbleGoalToast, PhaseToastText.AllBubblesToast);
    }

    // --- The sound ------------------------------------------------------------------------------

    /// <summary>Small, and the packet's restraint constraint is what makes that a fact worth
    /// pinning rather than a taste. The whole beat is budgeted at
    /// <see cref="BubbleCelebration.TotalSeconds"/>; the sound is the longest part of it and must
    /// fit inside.</summary>
    [Fact]
    public void Triumph_IsTheDocumentedLengthAndFitsInsideTheBeat()
    {
        float[] pcm = SfxLab.TriumphPcm();
        // 48 kHz is the project's pinned mix rate; 1.15 s of it.
        Assert.InRange(pcm.Length, 54_000, 56_500);
        Assert.True(pcm.Length / 48000.0 <= BubbleCelebration.TotalSeconds,
            "the sound outlasts the celebration it belongs to");
    }

    /// <summary>Headroom. <c>Render()</c> clamps to ±1, so a recipe that overshoots does not error
    /// — it hard-clips, which on four overlapping ringing notes is an audible crunch. Four tails
    /// overlap here by construction (the ring is longer than the note gap), so this is the
    /// assertion most likely to catch a retune.</summary>
    [Fact]
    public void Triumph_NeverClips()
    {
        float peak = 0f;
        foreach (float s in SfxLab.TriumphPcm())
            peak = Math.Max(peak, Math.Abs(s));
        Assert.InRange(peak, 0.2f, 0.95f);
    }

    /// <summary>It starts from silence and lands on silence. A buffer that ends mid-swing clicks
    /// on every playback, and the click is the loudest thing in a quiet cue.</summary>
    [Fact]
    public void Triumph_StartsAndEndsAtSilence()
    {
        float[] pcm = SfxLab.TriumphPcm();
        Assert.InRange(Math.Abs(pcm[0]), 0f, 0.001f);
        Assert.InRange(Math.Abs(pcm[^1]), 0f, 0.01f);
    }

    /// <summary>
    /// <b>The one property that makes it an arrival and not a pickup: it CLIMBS.</b> A coin-pickup
    /// blip is one or two notes with no direction; this is four notes walking up a major triad to
    /// the octave, and the climb is what says "that was the last one" rather than "you got a
    /// thing". Measured as the mean absolute-amplitude-weighted zero-crossing rate of three
    /// windows across the phrase — the assertion is the SHAPE with a healthy margin, not the
    /// numbers, so retuning <c>TriumphNotesHz</c> keeps it green and flattening it does not.
    /// </summary>
    [Fact]
    public void Triumph_ClimbsFromFirstNoteToLast()
    {
        float[] pcm = SfxLab.TriumphPcm();
        // Windows centred on the 1st, 2nd and 4th note onsets (0.000 s / 0.115 s / 0.345 s at
        // 48 kHz), each 60 ms long — short enough to sit inside one note's attack before the
        // next one enters, which is what keeps the estimate about that note.
        double first = ZeroCrossingHz(pcm, 480, 480 + 2880);
        double second = ZeroCrossingHz(pcm, 5520 + 480, 5520 + 480 + 2880);
        double last = ZeroCrossingHz(pcm, 16560 + 480, 16560 + 480 + 2880);
        Assert.True(second > first * 1.05,
            $"the second note must be above the first ({first:F0} Hz -> {second:F0} Hz)");
        Assert.True(last > second * 1.05,
            $"the phrase must climb to its last note ({second:F0} Hz -> {last:F0} Hz)");
    }

    /// <summary>The phrase sits in a bell's band. Wide bounds on purpose — the guard against a
    /// retune landing on a foghorn (too low) or a smoke alarm (too high), not a pin on Talon's
    /// taste, which owns everything inside them.</summary>
    [Fact]
    public void Triumph_RingsInAGlassBand()
    {
        float[] pcm = SfxLab.TriumphPcm();
        double first = ZeroCrossingHz(pcm, 480, 480 + 2880);
        Assert.InRange(first, 400.0, 2500.0);
    }

    /// <summary>Deterministic: unlike the goose this recipe has no noise source at all, so the
    /// triumph is byte-identical on every peer and every session by construction. Pinned anyway,
    /// because a retune that reached for a shimmer out of <c>Random</c> would make a cached stream
    /// disagree with a freshly rendered one, and per-shot variation is the playback jitter's job
    /// rather than the recipe's.</summary>
    [Fact]
    public void Triumph_IsTheSameSoundEveryTime()
    {
        float[] a = SfxLab.TriumphPcm();
        float[] b = SfxLab.TriumphPcm();
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i += 97)
            Assert.Equal(a[i], b[i], 0.000001f);
    }

    /// <summary>
    /// <b>It is a different sound from the pop it follows.</b> The player has just heard the pop
    /// cue ninety-nine times; a hundredth one an octave up would read as another tick of the
    /// counter. The two share no PCM and are not the same length.
    /// </summary>
    [Fact]
    public void Triumph_IsNotJustAnotherPop()
    {
        Assert.NotEqual(Sfx.Pop, Sfx.Triumph);
        // Sfx ordinals are pinned (EventResponse.Sound serialises this enum as an int in every
        // presentation .tres), so a member appended in the middle would silently re-point every
        // authored profile. Triumph is the newest and must be the highest.
        foreach (Sfx kind in Enum.GetValues<Sfx>())
            Assert.True((int)Sfx.Triumph >= (int)kind, $"{kind} sits above Triumph in the enum");
    }

    /// <summary>Fundamental frequency of <c>pcm[from..to)</c>, in Hz, by zero-crossing count.
    /// The triumph's partials are 10–30% of the fundamental and there is no noise in the recipe,
    /// so the sign of the raw signal IS the fundamental's here — the three-pole lowpass
    /// <c>HonkTests.Fundamental</c> needs for the goose's reed stack would only smear this
    /// one.</summary>
    private static double ZeroCrossingHz(float[] pcm, int from, int to)
    {
        const int rate = 48000;
        int crossings = 0;
        for (int i = from + 1; i < to; i++)
        {
            if ((pcm[i - 1] < 0 && pcm[i] >= 0) || (pcm[i - 1] >= 0 && pcm[i] < 0))
                crossings++;
        }
        return crossings * rate / (2.0 * (to - from));
    }
}
