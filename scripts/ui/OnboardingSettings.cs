using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// Persisted onboarding state, following the DisplaySettings.cs / TelemetryStore.cs pattern:
/// a static class backed by a section of the SAME "user://settings.cfg" the dispatch calls
/// out ("the same store DisplaySettings uses"). Holds the first-run "don't show this again"
/// flags: the playtest foreword and the anonymous-usage notice (Talon, 2026-08-30 note 15).
/// The How-to-Play overlay (spec §3b) used to be the third; its level-entry door was removed on
/// 2026-09-04 and the flag went with it.
///
/// Deliberately a single "dismissed" boolean, not a separate "has ever entered the level"
/// flag — the scenarios in §3b/§8.2 reduce to exactly one bit: show on every level entry
/// UNLESS the player ticked "don't show this again" and closed. A fresh install has no
/// config, so GetValue's default (false) means "show" — matches "first time a player ever
/// enters the level." A corrupt config fails <see cref="ConfigFile.Load"/> silently (Error,
/// no exception) and every GetValue below still returns its default, so corruption degrades
/// to "show again," never a crash or a permanent wedge (spec §8 scenario 2).
/// </summary>
public static class OnboardingSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "onboarding";

    /// <summary>
    /// <b>Every first-run panel in the build is off, whatever the persisted flags say</b> — set
    /// once at boot for a process that exists to produce an image file or to drive itself
    /// (<c>LaunchOptions.SuppressesFirstRunPanels</c>). Never persisted: it is a property of the
    /// RUN, not of the player.
    ///
    /// <para><b>The defect</b> (W7-8, found by W7-4 while doing something else, 2026-08-30): the
    /// first-run How-to-Play overlay covered every windowed <c>*Capture</c> run in <c>tests/</c>.
    /// Captures are how this repo verifies anything Talon judges by eye, and a covered capture is
    /// evidence of nothing while looking exactly like evidence of something. It was also
    /// intermittent for a reason no worktree could explain: <c>user://</c> resolves by project
    /// NAME, so every worktree on this machine shares one <c>settings.cfg</c> and whichever agent
    /// last ticked "don't show this again" decided whether the next agent's capture was covered.</para>
    ///
    /// <para><b>Why the gate is HERE and not at each panel's door.</b> There are already two
    /// first-run panels and a third arrives the same day (the anonymous-usage notice, Talon's note
    /// 15). A fix naming How-to-Play would be stale by tonight. This class is the one place the
    /// whole CATEGORY is decided, so a panel is covered by asking the question it already asks —
    /// and <c>FirstRunSuppressionTests</c> walks every <c>…Dismissed</c> property by reflection, so
    /// a new flag that forgets to route through <see cref="Gated"/> turns the suite red rather than
    /// quietly re-covering the captures.</para>
    /// </summary>
    public static bool SuppressFirstRunPanels { get; set; }

    /// <summary>The escape hatch, and the reason the suppression above is allowed to be a blanket:
    /// <c>--show-first-run-panels</c> forces every first-run panel ON, overriding both the
    /// suppression and the persisted dismissal. A capture whose SUBJECT is a first-run panel is a
    /// real need (W7-2 is building one today), and it is also how W7-8 photographed the covered
    /// frame it was fixing without writing to a <c>settings.cfg</c> four other packets were using.
    /// Wins over <see cref="SuppressFirstRunPanels"/> — an explicit ask always beats a default.</summary>
    public static bool ForceShowFirstRunPanels { get; set; }

    /// <summary>One place the run-level overrides are applied, so every flag below is the same
    /// three-way decision: forced on, suppressed off, or whatever the player persisted.</summary>
    private static bool Gated(bool stored) =>
        !ForceShowFirstRunPanels && (SuppressFirstRunPanels || stored);

    /// <summary><b>The on-entry goal line is off for this run</b> — the same run-level question
    /// every first-run panel asks, asked by the one transient line that has nothing persisted
    /// behind it (<c>PhaseToastText.BubbleGoalToast</c>, raised from Gameplay's client-UI block).
    ///
    /// <para><b>Why it lives here despite storing nothing.</b> The line is shown every session on
    /// purpose — it is a goal reminder, not a tutorial, so there is deliberately no
    /// <c>…Dismissed</c> flag and no "don't show this again". But it is still copy sitting over
    /// the frame during the exact seconds a capture is taken, which is the whole defect
    /// <see cref="SuppressFirstRunPanels"/> exists to close, and this class's rule is that the
    /// CATEGORY is decided in ONE place rather than at each panel's door. Re-deriving
    /// <see cref="Gated"/>'s three-way decision at the call site is precisely what that rule
    /// forbids.</para>
    ///
    /// <para><b>Named for what it is, so it deliberately does NOT ride the reflection walk.</b>
    /// <c>FirstRunSuppressionTests</c> asserts the rule over every public <c>…Dismissed</c>
    /// property; this is not one — nobody dismisses it and nothing is stored — and naming it as if
    /// it were, purely to be swept up by that walk, would buy coverage with a lie. It is covered
    /// by a named test in that same file instead.</para></summary>
    public static bool GoalLineSuppressed => Gated(stored: false);

    /// <summary>True once the player has ticked "don't show this again" and closed the
    /// playtest foreword (MainMenu, spec: playtest goals — performance, connection/
    /// networking, crash-free). False (the default) means it shows on the next MainMenu load.</summary>
    public static bool PlaytestForewordDismissed => Gated(_playtestForewordDismissed);
    private static bool _playtestForewordDismissed;

    /// <summary>True once the player has closed the first-run anonymous-usage notice
    /// (<see cref="UsageNoticePanel"/>) by any route — the acknowledge button, the X or ESC. False
    /// (the default) means the title screen raises it after the next press of Start.
    ///
    /// <para><b>This is an onboarding flag and nothing else.</b> It records that the player has
    /// been TOLD; it is not consent, it does not imply consent, and it is stored in a different
    /// section from the thing it describes on purpose. The consent state lives in
    /// <c>[telemetry] usage_consent</c>, is written only by the settings toggle, and is backed by
    /// its own durable witness (<c>TelemetryPaths.OptOutMarkerFile</c>). Losing this flag costs a
    /// player one extra notice; it can never cost them their opt-out.</para></summary>
    public static bool UsageNoticeSeen { get; private set; }

    public static void Load()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // Ok or not — defaults apply either way (see class doc)
        // Type-checked, not cast: the class doc promises corruption "never" crashes boot, and
        // that must hold for wrong-typed values too, not just unparseable files.
        // "how_to_play_dismissed" is deliberately NOT read any more (2026-09-04): the level's
        // first-run How-to-Play door was removed, so there is nothing left for it to gate. The key
        // is left in place rather than migrated away — an orphan bool in a config section costs
        // nothing, and deleting other people's settings.cfg entries to tidy up is a worse trade
        // than leaving one.
        Variant foreword = cfg.GetValue(Section, "playtest_foreword_dismissed", false);
        _playtestForewordDismissed = foreword.VariantType == Variant.Type.Bool && foreword.AsBool();
        Variant usage = cfg.GetValue(Section, "usage_notice_seen", false);
        UsageNoticeSeen = usage.VariantType == Variant.Type.Bool && usage.AsBool();
    }

    /// <summary>Records the live state of the playtest foreword's "don't show this again" toggle.
    /// Written on every toggle change rather than at close, so "closed without ticking" naturally
    /// leaves this untouched (still false) and "ticked then closed" persists true — no separate
    /// commit-on-close step needed.</summary>
    public static void SetPlaytestForewordDismissed(bool dismissed)
    {
        _playtestForewordDismissed = dismissed;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep display/telemetry/any other sections
        cfg.SetValue(Section, "playtest_foreword_dismissed", dismissed);
        cfg.Save(Path);
    }

    /// <summary>Records that the first-run usage notice has been shown and closed. Writes nothing
    /// about consent — see <see cref="UsageNoticeSeen"/>.</summary>
    public static void SetUsageNoticeSeen(bool seen)
    {
        UsageNoticeSeen = seen;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep display/telemetry/any other sections
        cfg.SetValue(Section, "usage_notice_seen", seen);
        cfg.Save(Path);
    }
}
