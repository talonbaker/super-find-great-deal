using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The first-run anonymous-usage notice, and the opt-out it must never undo.</b>
/// W7-2, 2026-08-30, against Talon's note 15.
///
/// <para>These are source-text assertions rather than behavioural ones, deliberately and with a
/// stated limit. The behaviour that matters — does an opt-out survive losing settings.cfg — needs
/// Godot's <c>ConfigFile</c>, <c>FileAccess</c> and a real <c>user://</c> root, so it lives in the
/// in-engine self-test (<c>TelemetrySelfTest.OptOutSurvivesLosingTheConfig</c>, run headless by
/// <c>tests/Run-TelemetryTest.ps1</c>) where it can actually delete a file and reload. What is
/// left for this file is the class of defect a runtime test cannot see: a LATER edit quietly
/// putting one of these rules back the way it was. Every check below therefore carries a positive
/// control, because an absence assertion from a scanner that has never matched anything is not
/// evidence.</para>
/// </summary>
public class UsageNoticeTests
{
    // =======================================================================================
    // 1. The notice is a notice. It must not be able to decide anything.
    // =======================================================================================

    /// <summary>
    /// <b>Neither the X nor the button may write consent — in either direction.</b>
    ///
    /// <para>Talon's shape for this panel is "a button to say don't show this message again with
    /// an X to click out", over a setting that is ON by default and that "only the player can turn
    /// off… in settings". Dismissing a notice is not a decision, so a dismissal that flipped
    /// consent either way would be the panel deciding on the player's behalf — the exact thing the
    /// design removed when it retired the old Allow / No thanks dialog.</para>
    /// </summary>
    [Fact]
    public void TheNotice_WritesNoConsent()
    {
        string panel = ReadRepoFile("scripts/ui/UsageNoticePanel.cs");
        // CODE only. The class doc names TelemetryStore.SetUsageConsent on purpose — it is where
        // it says the settings toggle is the sole writer — and a scanner that cannot tell an
        // explanation from a call would forbid the file from explaining itself.
        string code = StripDocComments(panel);
        Assert.DoesNotContain("SetUsageConsent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TelemetryStore", code, StringComparison.Ordinal);

        // The one thing it IS allowed to write: that the player has been told.
        Assert.Contains("OnboardingSettings.SetUsageNoticeSeen", code, StringComparison.Ordinal);
    }

    /// <summary>The positive control for the sweep above: the matcher can see the call it hunts,
    /// so "the panel does not contain it" is a fact about the panel and not about a broken
    /// scanner.</summary>
    [Fact]
    public void TheNoticeSweep_WouldActuallyFail()
    {
        const string Planted = "        TelemetryStore.SetUsageConsent(false);";
        Assert.Contains("SetUsageConsent", Planted, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The settings toggle is the only writer of consent in shipping code.</b> Talon: <i>"I
    /// would like this to be something that the player themselves has to go into settings and
    /// disable."</i>
    ///
    /// <para>Enumerated rather than asserted about one file, because the failure this guards is a
    /// NEW call site appearing somewhere plausible — a boot path, a first-run flow, an
    /// achievements screen — and deciding on the player's behalf. <c>TelemetryStore</c> itself is
    /// the definition, and the in-engine self-test is a test.</para>
    /// </summary>
    [Fact]
    public void OnlySettings_WritesConsent()
    {
        var callers = new List<string>();
        foreach (string file in EnumerateSource("scripts"))
        {
            string text = File.ReadAllText(file);
            if (!text.Contains("SetUsageConsent(", StringComparison.Ordinal))
                continue;
            string rel = Relative(file);
            if (rel is "scripts/telemetry/TelemetryStore.cs"        // the definition
                or "scripts/telemetry/TelemetrySelfTest.cs")        // the in-engine test
                continue;
            callers.Add(rel);
        }

        Assert.Equal(new[] { "scripts/ui/SettingsPanel.cs" }, callers.ToArray());
    }

    // =======================================================================================
    // 2. On by default — and the rule spelled in exactly one place
    // =======================================================================================

    /// <summary>
    /// <b>Nothing outside <c>TelemetryStore</c> may ask whether consent is <c>Granted</c>.</b>
    ///
    /// <para>Before 2026-08-30 the answer to "may we report" was <c>UsageConsent ==
    /// Consent.Granted</c>, written out at five call sites. Reporting is now on by DEFAULT, which
    /// means <c>Unset</c> is permissive — so every one of those spellings is now wrong, and the
    /// realistic failure is not getting them all: five sites agreeing and one that silently does
    /// not, which reads at runtime as reporting that works for most players and not for the ones
    /// who never touched the setting (or, far worse, the reverse). The rule lives in
    /// <c>UsageReportingEnabled</c> and callers ask that.</para>
    /// </summary>
    [Fact]
    public void NoCallerReadsTheRawConsentState()
    {
        var offenders = new List<string>();
        foreach (string file in EnumerateSource("scripts"))
        {
            string rel = Relative(file);
            if (rel is "scripts/telemetry/TelemetryStore.cs"        // where the tri-state is defined
                or "scripts/telemetry/TelemetrySelfTest.cs")        // and where it is asserted
                continue;
            foreach (string line in File.ReadAllLines(file))
            {
                if (Regex.IsMatch(line, @"UsageConsent\s*[!=]=\s*.*Consent\.Granted"))
                    offenders.Add($"{rel}: {line.Trim()}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>The positive control for the pattern above — it has to be able to see the
    /// comparison it forbids, in the exact shape the code used to use.</summary>
    [Fact]
    public void TheConsentComparisonSweep_WouldActuallyFail()
    {
        const string Planted = "if (TelemetryStore.UsageConsent == TelemetryStore.Consent.Granted)";
        Assert.Matches(@"UsageConsent\s*[!=]=\s*.*Consent\.Granted", Planted);
    }

    /// <summary><c>UsageReportingEnabled</c> is the default-on rule itself, so it is worth pinning
    /// as text: it must be defined against <c>Denied</c>, not against <c>Granted</c>. Written the
    /// other way ("== Granted") the property compiles, reads plausibly, and opts every player who
    /// never opened Settings back OUT — the same bug as before with a nicer name.</summary>
    [Fact]
    public void ReportingEnabled_IsDefinedAgainstDenied()
    {
        string store = ReadRepoFile("scripts/telemetry/TelemetryStore.cs");
        Assert.Contains("UsageReportingEnabled => UsageConsent != Consent.Denied",
            store, StringComparison.Ordinal);
    }

    // =======================================================================================
    // 3. The durable opt-out
    // =======================================================================================

    /// <summary>
    /// <b>The opt-out is written in two places, and the second one is not settings.cfg.</b>
    ///
    /// <para>Talon wrote this twice: <i>"Once they disable this setting, please do not reenable it
    /// on them restarting the game."</i> With reporting on by default, LOSING the stored state is
    /// what re-enables it — and <c>TelemetryStore.LoadSharedConfig</c> already moves an unparseable
    /// settings.cfg aside and restarts every section from defaults. So a decline writes a marker
    /// in a different file, and the marker may only ever turn reporting OFF.</para>
    /// </summary>
    [Fact]
    public void TheOptOut_HasASecondWitnessInADifferentFile()
    {
        string paths = ReadRepoFile("scripts/telemetry/TelemetryPaths.cs");
        Assert.Contains("OptOutMarkerFile", paths, StringComparison.Ordinal);
        // In the telemetry root, NOT in the settings file — the whole point is that losing one
        // does not lose the other.
        Assert.Matches(@"OptOutMarkerFile\s*=>\s*\$""\{Root\}/", paths);

        string store = ReadRepoFile("scripts/telemetry/TelemetryStore.cs");
        // Load consults the marker, and it is consulted for OFF only: the guarded branch sets
        // Denied. Nothing anywhere may set Granted from a marker's absence.
        Assert.Contains("if (OptOutMarkerExists())", store, StringComparison.Ordinal);
        // The marker turns reporting OFF and never on: the branch it guards assigns Denied.
        int guard = store.IndexOf("if (OptOutMarkerExists())", StringComparison.Ordinal);
        string branch = store.Substring(guard, 900);
        Assert.Contains("UsageConsent = Consent.Denied;", branch, StringComparison.Ordinal);
    }

    /// <summary>The self-test that actually exercises the loss paths must keep existing and keep
    /// being called. A durability guarantee whose only proof was deleted is a guarantee nobody is
    /// checking — and this one is the acceptance criterion Talon wrote twice.</summary>
    [Fact]
    public void TheOptOutDurability_IsExercisedInEngine()
    {
        string selfTest = ReadRepoFile("scripts/telemetry/TelemetrySelfTest.cs");
        Assert.Contains("OptOutSurvivesLosingTheConfig();", selfTest, StringComparison.Ordinal);
        Assert.Contains("ReportingIsOnByDefault();", selfTest, StringComparison.Ordinal);
        // Both loss modes, not just the tidy one.
        Assert.Contains("DELETED settings.cfg", selfTest, StringComparison.Ordinal);
        Assert.Contains("CORRUPTED settings.cfg", selfTest, StringComparison.Ordinal);
    }

    // =======================================================================================
    // 4. The copy against the code
    // =======================================================================================

    /// <summary>
    /// <b>The notice describes the report that is actually built.</b>
    ///
    /// <para>A privacy notice that overstates or understates what a game sends is a false
    /// statement to a player about their own data, and the way it becomes false is not a bad first
    /// draft — it is a field added to the payload a year later by someone who never opened the
    /// panel. So this pins the payload's field list. A new key in
    /// <c>TelemetryPayload.BuildUsage</c> or <c>Envelope</c> turns this red with the key's name in
    /// the failure, and the fix is to read the notice's copy and decide whether it still tells the
    /// truth — then add the key here.</para>
    ///
    /// <para>The mapping's right-hand column is the phrase in <c>UsageNoticePanel.tscn</c> that
    /// covers the key; it is checked to actually be present, so a copy rewrite that drops a
    /// disclosure also goes red.</para>
    /// </summary>
    [Fact]
    public void TheNoticeCopy_CoversEveryFieldTheUsageReportSends()
    {
        // key -> the words in the notice that disclose it. Empty string = deliberately not
        // disclosed as a separate item because it carries nothing about the player.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- envelope: identity and build -------------------------------------------
            ["device_id"] = "A random tag made on this machine",
            ["report_type"] = "",
            ["sent_at"] = "",
            ["build_version"] = "build's version",
            ["build_kind"] = "build's version",
            // --- envelope: the machine ---------------------------------------------------
            ["os"] = "Operating system and version",
            ["os_version"] = "Operating system and version",
            ["cpu"] = "CPU",
            ["gpu"] = "graphics card",
            ["ram_mb"] = "memory",
            ["resolution"] = "window resolution",
            ["renderer"] = "renderer",
            ["locale"] = "system language and region",
            ["region"] = "system language and region",
            // --- the session --------------------------------------------------------------
            ["session_length_sec"] = "How long you played",
            ["role"] = "whether you hosted or joined",
            ["transport"] = "joined, the transport,",
            ["player_count_seen"] = "how many players were in it",
            ["voice_used"] = "whether voice chat was used",
            ["props_grabbed"] = "picked up, threw and dropped",
            ["props_thrown"] = "picked up, threw and dropped",
            ["props_dropped"] = "picked up, threw and dropped",
            // --- LD-2 (2026-09-02): the cadence numbers, research §A2-R3 ------------------
            // Three durations: how much of the session the body spent standing still, the same
            // per minute, and the same split by level section. All three are a breakdown of the
            // session's length — "How long you played" is the clause that covers them — and none
            // says anything about WHO played. The packet did not authorise an edit to the notice
            // scene, so the clause is not extended; the report flags that a sentence naming
            // "standing still" would make the disclosure more precise, and the orchestrator can
            // route that edit. This mapping is what the copy honestly covers today.
            ["stop_seconds"] = "How long you played",
            ["stop_seconds_per_minute"] = "How long you played",
            ["stop_seconds_by_section"] = "How long you played",
        };

        string payload = ReadRepoFile("scripts/telemetry/TelemetryPayload.cs");
        // Both shapes the file uses to name a field: `d["key"] =` and the `{ "key", ... }`
        // initialiser inside Envelope.
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(payload, @"d\[""([a-z_]+)""\]\s*="))
            found.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(payload, @"\{\s*""([a-z_]+)""\s*,"))
            found.Add(m.Groups[1].Value);
        // Crash and feedback fields are their own prompts, not this setting's disclosure.
        foreach (string other in new[]
                 { "stack_trace", "exception_message", "log_tail", "detection", "answers", "text" })
            found.Remove(other);

        // The scanner has to have worked at all before its output can indict the copy.
        Assert.Contains("device_id", found);
        Assert.Contains("session_length_sec", found);

        string[] unlisted = System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Where(found, k => !expected.ContainsKey(k)));
        Assert.True(unlisted.Length == 0,
            "TelemetryPayload sends field(s) the usage notice was never checked against: "
            + string.Join(", ", unlisted)
            + " — read scenes/ui/UsageNoticePanel.tscn and decide whether it still tells the truth.");

        string notice = ReadRepoFile("scenes/ui/UsageNoticePanel.tscn");
        foreach (KeyValuePair<string, string> pair in expected)
        {
            if (pair.Value.Length == 0)
                continue;
            Assert.True(notice.Contains(pair.Value, StringComparison.Ordinal),
                $"the notice no longer discloses '{pair.Key}' (looked for: \"{pair.Value}\")");
        }
    }

    /// <summary>The notice states the two facts Talon asked for in so many words: that reporting
    /// is on by default, and that Settings is where it goes off — plus the promise the durable
    /// marker exists to keep.</summary>
    [Fact]
    public void TheNoticeCopy_SaysOnByDefaultAndWhereToTurnItOff()
    {
        string notice = ReadRepoFile("scenes/ui/UsageNoticePanel.tscn");
        Assert.Contains("on by default", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Settings", notice, StringComparison.Ordinal);
        Assert.Contains("stays off", notice, StringComparison.Ordinal);
        // The label the copy points at has to be the label the panel actually renders, or the
        // instruction sends the player looking for a row that is not there.
        Assert.Contains("ANONYMOUS USAGE REPORT", notice, StringComparison.Ordinal);
        Assert.Contains("\"ANONYMOUS USAGE REPORT\"",
            ReadRepoFile("scripts/ui/SettingsPanel.cs"), StringComparison.Ordinal);
        Assert.Contains("Don't show this message again", notice, StringComparison.Ordinal);
    }

    // =======================================================================================
    // 4b. Exactly once, and the three launches that say so
    // =======================================================================================

    /// <summary>
    /// <b>Acceptance criterion 7, at the level where it can actually be decided.</b> The notice
    /// appears exactly once on a fresh profile, after the first Start — not on the second launch,
    /// and not again after "don't show this message again".
    ///
    /// <para>Each of those is one input to <see cref="UsageNoticeGate.ShouldShow"/>, which is why
    /// the predicate was pulled out of <c>MainMenu.ShowOptions</c>: inside a tier transition
    /// the only way to check it is to launch a window and look, and "I looked and it did not
    /// appear" is the weakest evidence in the repo.</para>
    /// </summary>
    [Fact]
    public void TheNotice_ShowsOnceOnAFreshProfile()
    {
        // Launch 1, fresh profile, the first press of Start.
        Assert.True(UsageNoticeGate.ShouldShow(
            enteringOptionsTier: true, suppressPanels: false, alreadySeen: false,
            telemetryConfigured: true));

        // Same launch, coming back from Host / Join / Settings: the menu rebuilds already past the
        // title, so this is not a press of Start and must not raise it again.
        Assert.False(UsageNoticeGate.ShouldShow(
            enteringOptionsTier: false, suppressPanels: false, alreadySeen: false,
            telemetryConfigured: true));

        // Launch 2, after the notice was closed by ANY route — the button, the X or ESC all write
        // the same flag, which is what makes "not on the second launch" hold for all three.
        Assert.False(UsageNoticeGate.ShouldShow(
            enteringOptionsTier: true, suppressPanels: false, alreadySeen: true,
            telemetryConfigured: true));

        // A capture run, on any profile: never.
        Assert.False(UsageNoticeGate.ShouldShow(
            enteringOptionsTier: true, suppressPanels: true, alreadySeen: false,
            telemetryConfigured: true));

        // A build that cannot send anything must not announce that it does.
        Assert.False(UsageNoticeGate.ShouldShow(
            enteringOptionsTier: true, suppressPanels: false, alreadySeen: false,
            telemetryConfigured: false));
    }

    /// <summary>The gate is what the menu actually asks — a pure function nothing calls is a
    /// decision that still lives inline.</summary>
    [Fact]
    public void TheMenu_AsksTheGate()
    {
        string menu = ReadRepoFile("scripts/ui/menu/MainMenu.cs");
        Assert.Contains("UsageNoticeGate.ShouldShow(", menu, StringComparison.Ordinal);
        Assert.Contains("ScenePaths.UsageNoticePanel", menu, StringComparison.Ordinal);
    }

    // =======================================================================================
    // 5. Fullscreen must not be able to reach a test process
    // =======================================================================================

    /// <summary>
    /// <b>The fullscreen default is a runtime value, not a project setting.</b> W7-2's stop
    /// condition: every headed capture and windowed self-test in this repo shares one display with
    /// whatever else is running, so a fullscreen default that applied to all of them would break
    /// the verification of every packet in a wave rather than only this one.
    ///
    /// <para><c>display/window/size/mode</c> in project.godot would do exactly that. This asserts
    /// it is absent, and that the one place the mode is applied is guarded.</para>
    /// </summary>
    [Fact]
    public void Fullscreen_IsNotAProjectSetting()
    {
        string project = ReadRepoFile("project.godot");
        Assert.DoesNotContain("window/size/mode", project, StringComparison.Ordinal);
    }

    /// <summary>Positive control for the check above: the matcher can see the setting it hunts, in
    /// the exact spelling Godot writes.</summary>
    [Fact]
    public void TheProjectSettingSweep_WouldActuallyFail()
    {
        const string Planted = "window/size/mode=3";
        Assert.Contains("window/size/mode", Planted, StringComparison.Ordinal);
    }

    /// <summary><c>ApplyWindowMode</c> has exactly one shipping call site outside its own class and
    /// the settings panel, and it is the real-client branch of Boot. Enumerated rather than
    /// asserted about Boot alone, because the defect this guards is a SECOND call site appearing in
    /// a launch path a capture uses.</summary>
    [Fact]
    public void TheWindowMode_IsAppliedOnOneLaunchPath()
    {
        var callers = new List<string>();
        foreach (string file in EnumerateSource("scripts"))
        {
            if (!File.ReadAllText(file).Contains("ApplyWindowMode()", StringComparison.Ordinal))
                continue;
            string rel = Relative(file);
            if (rel is "scripts/DisplaySettings.cs")   // the definition, and SetFullscreen's apply
                continue;
            callers.Add(rel);
        }

        Assert.Equal(new[] { "scripts/Boot.cs" }, callers.ToArray());

        // And in Boot it sits under the client branch, beside the telemetry session that already
        // means "a real windowed player" — not in the unconditional display block above it, which
        // --ui-capture, --screen-demo and every windowed bot also run through.
        string boot = ReadRepoFile("scripts/Boot.cs");
        int session = boot.IndexOf("BeginClientSession();", StringComparison.Ordinal);
        int apply = boot.IndexOf("DisplaySettings.ApplyWindowMode();", StringComparison.Ordinal);
        Assert.True(session > 0 && apply > session,
            "ApplyWindowMode must sit inside Boot's real-client branch, after BeginClientSession");
    }

    // =======================================================================================
    // 6. Note 17 — three screens, three identities
    // =======================================================================================

    /// <summary>
    /// <b>The splash carries no SETTINGS and no QUIT.</b> Talon, note 17: <i>"take away the
    /// settings and quit and replace this with 'press start'."</i>
    ///
    /// <para>Asserted structurally rather than by reading the whole file: the two destinations are
    /// built in <c>BuildMenuColumn</c>, the main menu's builder, and <c>BuildPressStart</c> — the
    /// splash's — builds one Label and adds no control of any kind. The failure this guards is a
    /// later edit "helpfully" putting a Quit back on the first screen, which is the exact shape of
    /// the thing he called stupid.</para>
    /// </summary>
    [Fact]
    public void TheSplash_HasNoSettingsAndNoQuit()
    {
        string menu = ReadRepoFile("scripts/ui/menu/MainMenu.cs");

        string splash = Between(menu, "private void BuildPressStart()", "private void BuildMenuColumn()");
        string mainMenu = Between(menu, "private void BuildMenuColumn()", "private void WireFocusNeighbours()");

        // POSITIVE CONTROL, in the same invocation: the matcher finds both on the main menu.
        Assert.Contains("\"SETTINGS\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("\"QUIT\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("ScenePaths.SettingsMenu", mainMenu, StringComparison.Ordinal);

        // ...and neither on the splash.
        Assert.DoesNotContain("SETTINGS", splash, StringComparison.Ordinal);
        Assert.DoesNotContain("QUIT", splash, StringComparison.Ordinal);
        Assert.DoesNotContain("ScenePaths.SettingsMenu", splash, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuTextLink", splash, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuActionButton", splash, StringComparison.Ordinal);

        // The one thing that IS on it.
        Assert.Contains("PRESS START", splash, StringComparison.Ordinal);
    }

    /// <summary><b>Press start accepts a key, a click and a controller face button</b> — he wrote it
    /// as a prompt, not a labelled button, so anything a player might reasonably do has to work.
    /// And it is read from the raw event rather than through an action, because <b>no
    /// <c>ui_*</c> action may be written into project.godot</b> (the standing rule) and "any
    /// button" is not a binding anyone could usefully rebind.</summary>
    [Fact]
    public void PressStart_AcceptsKeyMouseAndPad()
    {
        string menu = ReadRepoFile("scripts/ui/menu/MainMenu.cs");
        string handler = Between(menu, "public override void _UnhandledInput", "private void BuildWordmark");

        Assert.Contains("InputEventKey", handler, StringComparison.Ordinal);
        Assert.Contains("InputEventMouseButton", handler, StringComparison.Ordinal);
        Assert.Contains("InputEventJoypadButton", handler, StringComparison.Ordinal);
        Assert.Contains("ShowOptions()", handler, StringComparison.Ordinal);
        // Only on the splash: a stray key on the main menu must not navigate for the player.
        Assert.Contains("if (_optionsTier)", handler, StringComparison.Ordinal);

        // And no new ui_* action was smuggled into the project file to do it.
        string project = ReadRepoFile("project.godot");
        Assert.DoesNotContain("ui_accept={", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ui_select={", project, StringComparison.Ordinal);
    }

    /// <summary>Positive control for the ui_* sweep: the matcher can see an action block written in
    /// the exact shape project.godot uses.</summary>
    [Fact]
    public void TheUiActionSweep_WouldActuallyFail()
    {
        const string Planted = "ui_accept={\n\"deadzone\": 0.2,";
        Assert.Contains("ui_accept={", Planted, StringComparison.Ordinal);
    }

    /// <summary><b>The two screens do not share a line of layout.</b> The complaint was that
    /// pressing start looked like nothing happened, so the guard is that the main menu's wordmark
    /// position, wordmark size and list origin are all DIFFERENT constants from the splash's — not
    /// that the file mentions two tiers.</summary>
    [Fact]
    public void TheTwoScreens_DoNotShareALayout()
    {
        string look = ReadRepoFile("scripts/ui/menu/MenuLook.cs");
        foreach (string name in new[]
                 { "MenuWordmarkTop", "MenuWordmarkScale", "MenuRuleTop", "MenuRuleWidth",
                   "MenuListTop", "PressStartSize" })
        {
            Assert.Contains(name, look, StringComparison.Ordinal);
        }

        // The header is genuinely smaller, not nominally so: a scale of 1 would satisfy a
        // "constant exists" check and change nothing on screen.
        Match scale = Regex.Match(look, @"MenuWordmarkScale\s*=\s*([0-9.]+)f");
        Assert.True(scale.Success, "MenuWordmarkScale must be a float literal");
        double value = double.Parse(scale.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(value, 0.3, 0.75);

        // And it genuinely moves: the header sits well above the splash's wordmark.
        string menu = ReadRepoFile("scripts/ui/menu/MainMenu.cs");
        Assert.Contains("menu ? MenuLook.MenuWordmarkTop : MenuLook.WordmarkTop",
            menu, StringComparison.Ordinal);
        Assert.Contains("MenuWordmarkTop = DesignHeight * 0.100f", look, StringComparison.Ordinal);
        Assert.Contains("WordmarkTop = DesignHeight * 0.300f", look, StringComparison.Ordinal);
    }

    /// <summary>The studio mark is the one part of the flow Talon said he liked, so the guard is
    /// that it stayed a separate scene that hands off to the menu rather than being folded into
    /// it.
    ///
    /// <para><b>EGG-2, 2026-09-02: the literal it used to assert is gone and that is the point.</b>
    /// It read <c>"[ STUDIO MARK ]"</c> — the placeholder Talon replaced with the studio's actual
    /// name in addendum §10. Re-asserting the placeholder would have made this test a lock on the
    /// thing being changed, so it now asserts the PROPERTY it was always about: the splash renders
    /// the studio mark from <see cref="MpFoundation.Ui.Branding.Studio"/> (one constant, not a
    /// literal typed into the screen) and still hands off to the title rather than being folded
    /// into it.</para></summary>
    [Fact]
    public void TheStudioMark_IsStillItsOwnScreen()
    {
        string splash = ReadRepoFile("scripts/ui/Splash.cs");
        Assert.Contains("title.Text = Branding.Studio;", splash, StringComparison.Ordinal);
        Assert.Contains("ScenePaths.Title", splash, StringComparison.Ordinal);
        // The mark is a name now, and it is Talon's name — asserted against the compiled constant
        // so a source scan cannot pass on a comment that merely mentions it.
        Assert.Equal("Great-Grand-Software", MpFoundation.Ui.Branding.Studio);
        Assert.Equal("Watis World", MpFoundation.Ui.Branding.Wordmark);
    }

    // =======================================================================================
    // helpers
    // =======================================================================================

    /// <summary>The source text between two markers — used to assert about ONE method rather than a
    /// whole file, so "the splash has no Quit" is a claim about the splash's builder and not about
    /// whether the word appears anywhere in a 400-line class.</summary>
    private static string Between(string source, string startMarker, string endMarker)
    {
        int a = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(a >= 0, $"marker not found: {startMarker}");
        int b = source.IndexOf(endMarker, a, StringComparison.Ordinal);
        Assert.True(b > a, $"marker not found after {startMarker}: {endMarker}");
        return source[a..b];
    }

    /// <summary>Drops <c>///</c> doc-comment lines so a source scan indicts calls rather than
    /// prose. A file that explains the rule it obeys must not be findable as a violation of
    /// it.</summary>
    private static string StripDocComments(string source)
    {
        var kept = new List<string>();
        foreach (string line in source.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    private static string ReadRepoFile(string relative) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<string> EnumerateSource(string relativeDir) =>
        Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), relativeDir), "*.cs",
            SearchOption.AllDirectories);

    private static string Relative(string absolute) =>
        Path.GetRelativePath(FindRepoRoot(), absolute).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
