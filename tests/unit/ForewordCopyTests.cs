using System;
using System.IO;
using System.Text.RegularExpressions;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>Talon's own words, pinned character-for-character.</b> COPY-1, 2026-09-04.
///
/// <para>On the day of the live playtest Talon dictated three UI changes and the copy came with a
/// standing instruction: <i>his phrasing, his punctuation, his spelling, verbatim.</i> That is a
/// harder thing to hold than it sounds — every line below contains at least one thing a careful
/// reader will want to fix. "psydo" is his spelling of "pseudo". "breath" is his spelling of
/// "breathe". "a pressing 'H'" has a stray article. The ellipsis after "experience" is <b>nine</b>
/// dots. The telemetry phrase is wrapped in literal markdown asterisks that this panel renders as
/// asterisks. None of it is a typo to be swept, and a well-meaning tidy-up is exactly the drift
/// this file exists to stop.</para>
///
/// <para><b>Why the scene file is read as text.</b> The foreword's copy lives in
/// <c>scenes/ui/PlaytestForewordPanel.tscn</c>, which no Godot-free test can instantiate. Reading
/// the shipped scene as text pins <b>what a player actually sees</b>, which is strictly better
/// evidence than a C# constant that a scene may or may not still be wired to — the failure mode
/// where the constant is right and the screen is wrong is the one that matters. Same technique as
/// <c>UsageNoticeTests</c>, same repo-root walk.</para>
///
/// <para>The last group pins the <b>WINDOWED</b> display row, whose whole point is an inversion:
/// the label and the binding negate while <c>DisplaySettings.Fullscreen</c> keeps its meaning. An
/// inversion is the easiest thing in this file to "fix" back into a bug, in either direction.</para>
/// </summary>
public class ForewordCopyTests
{
    // ==========================================================================================
    // Talon's words. Do not edit anything between here and the closing brace of ExpectedBody
    // without his say-so — an edit here is an edit to shipped copy, not to a test fixture.
    // ==========================================================================================

    private const string ExpectedTitle = "Watis this game?";

    private const string ExpectedBody =
        "Pop bubbles...with friends! Can YOU pop them all...?\n"
        + "\n"
        + "No, you cannot pop them all because I've hidden some of the bubbles in places impossible to find. So just run around, enjoy! Check out what's on TV.\n"
        + "\n"
        + "When it gets dark, don't forget to press 'F' to turn on your flashlight or you'll get caught by the Night Lurker!\n"
        + "\n"
        + "Proximity voice is part of this playtest. For the mic-less or shy, a pressing 'H' will act as a kind of psydo-proximity voice for testing purposes.\n"
        + "\n"
        + "Also, I'm collecting **anonymous telemetry playtest data** for improving the game experience.........and I'm 100% going to sell that data the moment I get the chance, so you should opt out now, which you can do from the settings menu!\n"
        + "\n"
        + "Thank you for playing.\n"
        + "\n"
        + "Love,\n"
        + "Talon.";

    private static readonly string[] ExpectedNotes =
    {
        "Double tapping a directional key will let you run in that direction.",
        "Water will kill you if you breath it in. The game is realistic like that.",
        "The bubbles will also kill you, but I coated the player in a fine dusting of salt which "
            + "kills the bubbles first. I've included a bubble death counter for your satisfaction.",
    };

    // --- the foreword panel --------------------------------------------------------------------

    [Fact]
    public void TheForeword_TitleIsTalonsHeading()
    {
        Assert.Equal(ExpectedTitle, NodeText("Title"));
    }

    /// <summary>The whole body, in one comparison, so a reworded paragraph and a deleted blank
    /// line fail the same way. Split into per-paragraph assertions this would still pass with the
    /// paragraphs reordered.</summary>
    [Fact]
    public void TheForeword_BodyIsTalonsWordsExactly()
    {
        Assert.Equal(ExpectedBody, NodeText("Body"));
    }

    /// <summary>The details a proofreader removes on sight, each named so a failure says WHICH one
    /// went. <see cref="TheForeword_BodyIsTalonsWordsExactly"/> already covers them; this exists so
    /// the failure message is a sentence rather than a 700-character diff.</summary>
    [Theory]
    [InlineData("**anonymous telemetry playtest data**", "the literal markdown asterisks")]
    [InlineData("psydo-proximity", "Talon's spelling of 'pseudo'")]
    [InlineData("experience.........and", "the nine-dot ellipsis")]
    [InlineData("Night Lurker", "his player-facing name for the night creature")]
    [InlineData("a pressing 'H'", "his article, kept")]
    [InlineData("Love,\nTalon.", "the sign-off, on two lines")]
    public void TheForeword_KeepsTheDetailAProofreaderWouldRemove(string fragment, string what)
    {
        Assert.True(NodeText("Body").Contains(fragment, StringComparison.Ordinal),
            $"The foreword body no longer contains {what}: \"{fragment}\"");
    }

    /// <summary>The blank lines are structure, not whitespace: this is a letter, and the paragraphs
    /// are how it reads as one. A <c>.tscn</c> round-tripped through a tool that trims runs of
    /// newlines would pass every fragment check above and still lose the shape.</summary>
    [Fact]
    public void TheForeword_KeepsSixBlankLines()
    {
        Assert.Equal(6, Regex.Matches(NodeText("Body"), "\n\n").Count);
    }

    /// <summary>The rendered panel is one Label per Talon's prose, so the retired three-goal
    /// scaffolding must be gone rather than merely emptied — an orphaned Heading with no Body is
    /// how a "collapsed" section comes back.</summary>
    [Fact]
    public void TheForeword_NoLongerCarriesTheThreeGoalScaffolding()
    {
        string scene = ReadRepoFile("scenes/ui/PlaytestForewordPanel.tscn");
        Assert.DoesNotContain("name=\"Goals\"", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Intro\"", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Footer\"", scene, StringComparison.Ordinal);
    }

    /// <summary>What the rewrite must NOT take with it: the panel's own chrome. Talon replaced the
    /// words, not the door.</summary>
    [Fact]
    public void TheForeword_KeepsItsScrimCloseAndDontShowRow()
    {
        string scene = ReadRepoFile("scenes/ui/PlaytestForewordPanel.tscn");
        Assert.Contains("name=\"Scrim\"", scene, StringComparison.Ordinal);
        Assert.Contains("name=\"CloseButton\"", scene, StringComparison.Ordinal);
        Assert.Contains("name=\"DontShowRow\"", scene, StringComparison.Ordinal);
        Assert.Contains("Press ESC to close", scene, StringComparison.Ordinal);
        // Theme variations, not values: the body is still inside the kit (UI-DESIGN-SYSTEM).
        Assert.Contains("theme_type_variation = &\"Body\"", scene, StringComparison.Ordinal);
    }

    /// <summary>The positive control. An absence assertion from a reader that has never matched
    /// anything is not evidence, and neither is an equality assertion against a string the reader
    /// silently failed to find — <see cref="NodeText"/> returning "" would make several of the
    /// checks above pass for the wrong reason. This proves the reader sees the real text and that
    /// a one-character edit to it fails.</summary>
    [Fact]
    public void TheSceneReader_WouldActuallyFail()
    {
        string scene = ReadRepoFile("scenes/ui/PlaytestForewordPanel.tscn");
        Assert.Equal(ExpectedBody, ExtractNodeText(scene, "Body"));

        // "psydo" tidied to "pseudo" — the single likeliest well-meaning edit in the file.
        string tidied = scene.Replace("psydo-proximity", "pseudo-proximity", StringComparison.Ordinal);
        Assert.NotEqual(scene, tidied);
        Assert.NotEqual(ExpectedBody, ExtractNodeText(tidied, "Body"));

        // A whole paragraph deleted.
        string cut = scene.Replace("Thank you for playing.\n\n", "", StringComparison.Ordinal);
        Assert.NotEqual(ExpectedBody, ExtractNodeText(cut, "Body"));
    }

    // --- GOOD TO KNOW ---------------------------------------------------------------------------

    [Fact]
    public void GoodToKnow_IsTalonsThreeLinesInHisOrder()
    {
        Assert.Equal("GOOD TO KNOW", HowToPlayContent.NotesTitle);
        Assert.Equal(ExpectedNotes, HowToPlayContent.Lines);
    }

    /// <summary>Named separately for the same reason as the foreword fragments: "breath" is one
    /// keystroke from being corrected, and a failure that says so beats a failure that prints two
    /// 160-character strings.</summary>
    [Fact]
    public void GoodToKnow_KeepsHisSpellingOfBreathe()
    {
        Assert.Contains("if you breath it in", HowToPlayContent.Lines[1], StringComparison.Ordinal);
    }

    /// <summary><b>The joke stands, and it is not a claim about an unbuilt feature.</b> Talon,
    /// 2026-09-04: <i>"About the 'death counter' I'm making a joke about the HUD bubble counter.
    /// Just a joke you know."</i> The counter it riffs on is <c>Hud.HudBubbleCount</c>, already on
    /// screen. Pinned because the line reads, to anyone arriving without that quote, exactly like
    /// copy promising a system the build lacks — and the fix they would reach for is deleting
    /// Talon's sentence.</summary>
    [Fact]
    public void GoodToKnow_KeepsTheBubbleDeathCounterJoke()
    {
        Assert.Contains("bubble death counter", HowToPlayContent.Lines[2], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "scripts", "ui", "hud", "HudBubbleCount.cs")),
            "The joke's referent — the shipped HUD bubble counter — is gone; the line now promises "
            + "something that really does not exist. Ask Talon before touching either.");
    }

    // --- the WINDOWED display row -----------------------------------------------------------------

    /// <summary>Label, initial state and handler all negate together. Any one of the three left
    /// un-negated is a toggle that lies, and two of the three combinations lie silently.</summary>
    [Fact]
    public void TheDisplayRow_SaysWindowedAndInvertsTheBinding()
    {
        string panel = ReadRepoFile("scripts/ui/SettingsPanel.cs");
        Assert.Contains("Text = \"WINDOWED\",", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"FULLSCREEN\",", panel, StringComparison.Ordinal);
        Assert.Contains("ButtonPressed = !DisplaySettings.Fullscreen,", panel, StringComparison.Ordinal);
        Assert.Contains("DisplaySettings.SetFullscreen(!on)", panel, StringComparison.Ordinal);
    }

    /// <summary>The stored setting keeps its meaning — only the row negates. If someone ever
    /// "simplifies" this by inverting <c>DisplaySettings</c> itself, every other reader of it
    /// (Boot's real-client branch, settings.cfg, the export) flips with it and the game boots
    /// windowed for everyone. That edit must fail here.</summary>
    [Fact]
    public void TheStoredSetting_StillMeansFullscreen()
    {
        string display = ReadRepoFile("scripts/DisplaySettings.cs");
        Assert.Contains("public static bool Fullscreen { get; private set; } = true;", display, StringComparison.Ordinal);
        Assert.Contains("? DisplayServer.WindowMode.Fullscreen", display, StringComparison.Ordinal);
        Assert.Contains("cfg.GetValue(Section, \"fullscreen\", true)", display, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>ToggleMode before ButtonPressed, on every code-built pill.</b>
    ///
    /// <para>Godot's <c>BaseButton::set_pressed</c> returns immediately when <c>toggle_mode</c> is
    /// false, and <see cref="PillToggle"/> sets <c>ToggleMode</c> in <c>_Ready</c> — which has not
    /// run when an object initializer executes. Every pill built without the explicit
    /// <c>ToggleMode = true</c> therefore opened UNCHECKED regardless of the setting behind it.
    /// Measured headed on 2026-09-04 against a profile reading <c>fullscreen=true</c> and no
    /// opt-out marker: FULLSCREEN read off over a fullscreen game, and ANONYMOUS USAGE REPORT read
    /// off over a session that was reporting. Talon reported the first; the second was worse.</para>
    ///
    /// <para>Asserted structurally rather than per-row so a FOURTH pill added later cannot
    /// reintroduce it.</para>
    /// </summary>
    [Fact]
    public void EveryCodeBuiltPill_SetsToggleModeBeforeButtonPressed()
    {
        foreach (Match m in PillInitializers(ReadRepoFile("scripts/ui/SettingsPanel.cs")))
        {
            // CODE only. The comments here explain the ordering and therefore name both
            // properties; a scanner that cannot tell an explanation from an assignment reads
            // "ButtonPressed" out of the note warning about it. (Caught by this test on its
            // first run, against a correct file — the positive control that was not planned.)
            string body = StripComments(m.Groups[1].Value);
            int toggleMode = body.IndexOf("ToggleMode = true", StringComparison.Ordinal);
            int pressed = body.IndexOf("ButtonPressed", StringComparison.Ordinal);
            if (pressed < 0)
                continue; // a pill that sets no initial state cannot be wrong about one
            Assert.True(toggleMode >= 0,
                "A PillToggle initializer sets ButtonPressed without ToggleMode = true; it will "
                + "open unchecked whatever the setting says:\n" + body);
            Assert.True(toggleMode < pressed,
                "ToggleMode must be assigned BEFORE ButtonPressed in the initializer:\n" + body);
        }
    }

    /// <summary>Positive control for the sweep above, on the real file with the guard removed from
    /// one row. Without this, "all three pills are fine" could equally mean "the regex found
    /// nothing".</summary>
    [Fact]
    public void ThePillSweep_WouldActuallyFail()
    {
        string panel = ReadRepoFile("scripts/ui/SettingsPanel.cs");
        Assert.Equal(3, PillInitializers(panel).Count);

        string planted = panel.Replace(
            "            ToggleMode = true, // before ButtonPressed, always\n",
            "", StringComparison.Ordinal);
        Assert.NotEqual(panel, planted);

        bool caught = false;
        foreach (Match m in PillInitializers(planted))
        {
            string body = StripComments(m.Groups[1].Value);
            if (body.Contains("ButtonPressed", StringComparison.Ordinal)
                && !body.Contains("ToggleMode = true", StringComparison.Ordinal))
                caught = true;
        }
        Assert.True(caught, "The pill sweep cannot see a missing ToggleMode, so its passes mean nothing.");
    }

    // --- plumbing --------------------------------------------------------------------------------

    /// <summary>Every <c>new PillToggle { … }</c> initializer body in a source file.</summary>
    private static MatchCollection PillInitializers(string source) =>
        Regex.Matches(source, @"new PillToggle\s*\{(.*?)\};", RegexOptions.Singleline);

    /// <summary>Line comments out, so a note ABOUT a property is never read as an assignment to
    /// it. None of these initializers contain a string literal with "//" in it, which the
    /// positive control exercises.</summary>
    private static string StripComments(string code) =>
        Regex.Replace(code, @"//[^\r\n]*", string.Empty);

    private static string NodeText(string nodeName) =>
        ExtractNodeText(ReadRepoFile("scenes/ui/PlaytestForewordPanel.tscn"), nodeName);

    /// <summary>The <c>text</c> property of one node in a <c>.tscn</c>. Godot writes multi-line
    /// strings with real newlines, so the value runs to the next unescaped quote — none of this
    /// copy contains one, which is asserted rather than assumed.</summary>
    private static string ExtractNodeText(string scene, string nodeName)
    {
        Match node = Regex.Match(
            scene,
            @"\[node name=""" + Regex.Escape(nodeName) + @"""[^\]]*\](.*?)(?=\r?\n\[node |\z)",
            RegexOptions.Singleline);
        Assert.True(node.Success, $"No [node name=\"{nodeName}\"] in PlaytestForewordPanel.tscn.");

        Match text = Regex.Match(node.Groups[1].Value, "\r?\ntext = \"([^\"]*)\"", RegexOptions.Singleline);
        Assert.True(text.Success, $"Node \"{nodeName}\" has no text property.");
        return text.Groups[1].Value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relative) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
