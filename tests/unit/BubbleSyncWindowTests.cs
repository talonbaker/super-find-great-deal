using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Sail.Game.Bubble;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>FIX-1 (2026-09-04): the pins that keep <c>tests/Run-BubbleSyncTest.ps1</c>'s late joiner
/// alive long enough for its own assertions to mean anything.</b>
///
/// <para>The defect these exist to close was not a red test. It was a GREEN half of one. The
/// suite's assertion 6 asserts that a late joiner sees the reset broadcast return every bubble;
/// the late joiner was handed a typed 26 s lifetime measured from its own launch, while the reset
/// fired 30 s + the lever's two-stage confirm into the SERVER'S clock, and it exited 141 ms before
/// the reset was observable on any peer. Measured on `main` @ c16414d, 2026-09-04 18:02 UTC: C's
/// last sample 18:02:32.199Z, the reset's effect first observable 18:02:32.340Z. The assertion had
/// therefore never executed once, in any run, ever — and the shipping repo believed the one
/// capability the courier economy is built on was covered.</para>
///
/// <para><b>Why these are xUnit and not more PowerShell.</b> A scene suite cannot pin its own
/// composition: the very failure mode here is a suite that runs, exits 0 on six of seven groups,
/// and reports the seventh as a game defect. These are structural facts about the harness — read
/// off its source — and they run in a second, in the half of the suite that is not the one under
/// discussion.</para>
/// </summary>
public class BubbleSyncWindowTests
{
    private const string HarnessPath = "tests/Run-BubbleSyncTest.ps1";
    private const string FixturePath = "scripts/game/bubble/BubbleSelfTest.cs";

    /// <summary><b>The pin, and the whole of FIX-1 as a fact.</b> Not one bot in this suite may be
    /// handed a <c>--duration</c> that was typed. Every one must come from
    /// <c>Get-BotDurationArg</c>, which subtracts the bot's launch offset from a session end
    /// derived from the server's own announced reset schedule — so moving
    /// <c>--bubble-reset-at</c>, <c>MinDwellSec</c> or <c>ConfirmWindowSec</c> moves the sampling
    /// windows with it instead of silently making assertion 6 vacuous again.</summary>
    [Fact]
    public void EveryBotLifetimeInTheBubbleSuiteIsDerived_NotTyped()
    {
        string text = ReadRepoFile(HarnessPath);
        var args = new List<string>();
        foreach (Match m in Regex.Matches(text, "\"--duration\",\\s*([^,]+),"))
            args.Add(m.Groups[1].Value.Trim());

        Assert.Equal(3, args.Count); // the walker, the witness and the late joiner
        foreach (string arg in args)
        {
            Assert.True(
                arg.Contains("Get-BotDurationArg") || arg.Contains("$lateDurationArg"),
                $"{HarnessPath} hands a bot --duration {arg}, which is not derived from the "
                + "server's announced reset schedule. A typed lifetime beside a typed reset mark "
                + "is the FIX-1 defect: the late joiner exits before the broadcast it is asserted "
                + "against, and its assertion silently stops running.");
        }

        // The two constants the typed windows used to live in are gone, not merely unused.
        Assert.DoesNotContain("$WitnessDurationSec", text);
        Assert.DoesNotContain("$LateDurationSec", text);
    }

    /// <summary>NET-1 §11.2 rule 1: the scheduled server-side event is gated on the SERVER'S own
    /// log before any bot is allowed to exit. The suite always did this to LAUNCH the late joiner
    /// (the two pop gates); FIX-1 makes it do the same to RETAIN it. Position matters — a gate
    /// after the exits proves nothing — so the ordering is what is pinned.</summary>
    [Fact]
    public void TheHarnessGatesOnTheServersResetBeforeItWaitsForAnyBotToExit()
    {
        string text = ReadRepoFile(HarnessPath);
        int gate = text.IndexOf("Wait-ForLogLine $serverOut \"^\\[bubbletest\\] reset\\s*$\"",
            System.StringComparison.Ordinal);
        int exits = text.IndexOf(".Proc.WaitForExit(", System.StringComparison.Ordinal);
        Assert.True(gate >= 0, $"{HarnessPath} no longer gates on the server's own reset line");
        Assert.True(exits >= 0, $"{HarnessPath} no longer waits for its bots to exit");
        Assert.True(gate < exits,
            "the reset gate must come BEFORE the bots are waited on, or a reset that never fired "
            + "surfaces as 'the late joiner never saw the reset' and accuses replication.");
    }

    /// <summary>Assertion 8's own reason to exist: the suite must be able to say
    /// <i>"the window closed early, this is a HARNESS defect"</i> rather than
    /// <i>"the broadcast never arrived"</i>. That distinction is the packet, and it is a string in
    /// the harness — so it is pinned as one.</summary>
    [Fact]
    public void TheHarnessDiscriminatesAClosedWindowFromAReplicationDefect()
    {
        string text = ReadRepoFile(HarnessPath);
        Assert.Contains("sampling window CLOSED", text);
        Assert.Contains("HARNESS defect, not a replication", text);
        Assert.Contains("VACUOUS", text);
    }

    /// <summary>The seam between the two files: the fixture prints the schedule and the harness
    /// parses it. A prefix that drifted on either side would fail the run loudly (the harness
    /// <c>Write-Fail</c>s when the line is absent), but it would fail 30 s in and look like an
    /// engine problem. Cheaper to catch here.</summary>
    [Fact]
    public void TheFixtureAnnouncesTheScheduleTheHarnessParses()
    {
        Assert.Equal("[bubbletest] reset schedule:", BubbleSelfTest.ResetSchedulePrefix);

        string fixtureText = ReadRepoFile(FixturePath);
        Assert.Contains("resetAt=", fixtureText);
        Assert.Contains("confirmDelay=", fixtureText);
        Assert.Contains("resetLandsAt=", fixtureText);

        string harness = ReadRepoFile(HarnessPath);
        Assert.Contains("\\[bubbletest\\] reset schedule:", harness);
        Assert.Contains("resetAt=([0-9.]+)s confirmDelay=([0-9.]+)s resetLandsAt=([0-9.]+)s", harness);
    }

    /// <summary>What makes <c>resetLandsAt</c> a real mark rather than a hopeful one: the fixture's
    /// arm→confirm gap must be past <c>MinDwellSec</c> (or the confirm is swallowed as a double
    /// tap, and no reset ever lands) and inside <c>ConfirmWindowSec</c> (or the arm has lapsed, and
    /// again no reset lands). Either failure would move the reset off the announced schedule the
    /// bot windows are now sized from — the pin that keeps the derivation honest at its source.
    /// </summary>
    [Fact]
    public void TheAnnouncedConfirmDelayLiesStrictlyInsideTheLeversOwnWindow()
    {
        Assert.True(BubbleSelfTest.ConfirmDelaySec > BubbleResetConfirm.MinDwellSec,
            "a confirm this soon is swallowed as a double tap and no reset lands at all");
        Assert.True(BubbleSelfTest.ConfirmDelaySec < BubbleResetConfirm.ConfirmWindowSec,
            "a confirm this late finds a lapsed arm and no reset lands at all");
        Assert.Equal(
            (BubbleResetConfirm.MinDwellSec + BubbleResetConfirm.ConfirmWindowSec) / 2.0,
            BubbleSelfTest.ConfirmDelaySec, 6);
    }

    /// <summary><b>The positive control.</b> Four of the five facts above are about text NOT being
    /// present, or about text being present in a file that must be the right file. A path typo
    /// would make all of them pass against an empty string. This proves the reads land on the real
    /// harness and the real fixture, with tokens that indisputably live there.</summary>
    [Fact]
    public void PositiveControl_TheseAuditsReadTheRealHarnessAndTheRealFixture()
    {
        string harness = ReadRepoFile(HarnessPath);
        Assert.Contains("BUBBLE SYNC TEST OVERALL", harness);
        Assert.Contains("--bubble-selftest", harness);
        Assert.True(harness.Length > 10_000, "the harness read back far too short — wrong path");

        string fixtureText = ReadRepoFile(FixturePath);
        Assert.Contains("class BubbleSelfTest", fixtureText);
        Assert.Contains("ClientForgeDelaySec", fixtureText);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
