using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// Local telemetry state, following the DisplaySettings.cs pattern: a static class backed by a
/// new [telemetry] section of user://settings.cfg (ConfigFile). Holds the pseudonymous device
/// id (a random GUID created once on first launch and persisted forever) and the tri-state
/// usage-report consent (unset / granted / denied). No login, email, or name — nothing
/// identifying (spec decision 3).
///
/// <para><b>2026-08-30 — reporting is ON BY DEFAULT and the opt-out is durable.</b> Talon's
/// note 15 moved this from opt-in ("ask once at first launch") to opt-out ("on by default to help
/// the development process… only the player can turn it off"), plus a first-run NOTICE
/// (<see cref="MpFoundation.Ui.UsageNoticePanel"/>) that says so. Two consequences, both
/// deliberate:</para>
/// <list type="number">
///   <item><b>The tri-state stays, and now earns its keep.</b> <c>Unset</c> means "never decided"
///   and reads as ON; <c>Denied</c> means "the player said no". Collapsing them to a bool would
///   make "no stored value" and "explicitly declined" the same fact, and only one of them may
///   default to on.</item>
///   <item><b>A decline is written twice, in two files.</b> See
///   <see cref="TelemetryPaths.OptOutMarkerFile"/>. With default-on, LOSING state is what
///   re-enables reporting, and settings.cfg is a file this codebase already moves aside on a
///   parse failure. The marker is the witness that survives that.</item>
/// </list>
/// <para>Read the answer through <see cref="UsageReportingEnabled"/>, never by comparing to
/// <c>Granted</c> — the whole point of the flip is that <c>Unset</c> is now permissive.</para>
/// </summary>
public static class TelemetryStore
{
    private const string Section = "telemetry";

    public enum Consent { Unset, Granted, Denied }

    /// <summary>Pseudonymous per-device GUID. Non-empty after <see cref="Load"/>.</summary>
    public static string DeviceId { get; private set; } = "";

    /// <summary>Persistent usage-report consent. Never compare this to <c>Granted</c> to decide
    /// whether to report — use <see cref="UsageReportingEnabled"/>, which knows that
    /// <c>Unset</c> is permissive since 2026-08-30.</summary>
    public static Consent UsageConsent { get; private set; } = Consent.Unset;

    /// <summary><b>The one question every caller actually asks.</b> True unless the player has
    /// explicitly turned reporting off. A single expression so the default-on rule cannot be
    /// half-applied: before this existed, five call sites each spelled out
    /// <c>== Consent.Granted</c>, and flipping the default by editing four of them would have left
    /// one that silently disagreed.</summary>
    public static bool UsageReportingEnabled => UsageConsent != Consent.Denied;

    /// <summary>Loads (and, on first launch, mints + persists the device id) from settings.cfg.</summary>
    public static void Load()
    {
        var cfg = LoadSharedConfig();

        Variant deviceId = cfg.GetValue(Section, "device_id", "");
        DeviceId = deviceId.VariantType == Variant.Type.String ? deviceId.AsString().Trim() : "";
        if (DeviceId.Length == 0)
        {
            DeviceId = System.Guid.NewGuid().ToString();
            cfg.SetValue(Section, "device_id", DeviceId);
            cfg.Save(TelemetryPaths.SettingsFile);
        }

        Variant consent = cfg.GetValue(Section, "usage_consent", "unset");
        UsageConsent = (consent.VariantType == Variant.Type.String ? consent.AsString() : "unset") switch
        {
            "true" => Consent.Granted,
            "false" => Consent.Denied,
            _ => Consent.Unset,
        };

        // THE DURABLE OPT-OUT (Talon, twice). The marker outranks settings.cfg in one direction
        // only — it can turn reporting OFF, never on — so a stale or hand-planted marker cannot
        // silence a player who has since opted back in (SetUsageConsent(true) deletes it), while a
        // lost, reset or corrupted settings.cfg cannot opt a declining player back IN.
        if (OptOutMarkerExists())
        {
            if (UsageConsent != Consent.Denied)
            {
                // The two witnesses disagree, which means settings.cfg lost the answer. Restate it
                // there rather than leaving a file that says "on" beside a marker that says "off":
                // the settings panel, a later reader and a support conversation should not have to
                // know which one wins.
                GD.Print("[telemetry] usage reporting is OFF: settings.cfg had no record of the "
                         + "opt-out, the durable marker did. Restoring the setting.");
                UsageConsent = Consent.Denied;
                cfg.SetValue(Section, "usage_consent", "false");
                cfg.Save(TelemetryPaths.SettingsFile);
            }
        }
        else if (UsageConsent == Consent.Denied)
        {
            // settings.cfg says denied but the marker is gone (a hand-edited profile, a partial
            // restore). Re-establish the second witness so the NEXT loss of settings.cfg is
            // survivable too. Never the reverse: a missing marker never grants consent.
            WriteOptOutMarker();
        }
    }

    /// <summary>Records the player's usage-report choice (persistent toggle — decision 5).
    /// Revoking also applies that decision to everything ALREADY queued: usage reports are
    /// deleted, and any other queued report is stripped of the hardware/locale snapshot it was
    /// built with. Consent has to govern the data at send time, not only at build time — the
    /// durable queue outlives the decision that filled it.</summary>
    public static void SetUsageConsent(bool granted)
    {
        UsageConsent = granted ? Consent.Granted : Consent.Denied;
        var cfg = LoadSharedConfig(); // keep device_id and any other sections
        cfg.SetValue(Section, "usage_consent", granted ? "true" : "false");
        cfg.Save(TelemetryPaths.SettingsFile);
        // The second witness, written BEFORE the queue purge and removed only on an explicit
        // grant. Order matters on the off path: if the process dies between the two, the state on
        // disk says "off" with some reports still queued — which the flush backstop then purges.
        // The other order would leave a player opted out on this launch and opted back IN on the
        // next one, which is the exact outcome Talon wrote twice to forbid.
        if (granted)
            RemoveOptOutMarker();
        else
            WriteOptOutMarker();
        if (!granted)
            PendingQueue.ApplyRevokedConsent();
    }

    private static bool OptOutMarkerExists() =>
        Godot.FileAccess.FileExists(TelemetryPaths.OptOutMarkerFile);

    /// <summary>Writes the durable opt-out witness. Its content is never read — only existence is
    /// the fact, so there is nothing in it to corrupt into a false "on" — but a human who opens
    /// the file deserves to be told what it is and how to undo it.</summary>
    private static void WriteOptOutMarker()
    {
        try
        {
            TelemetryPaths.EnsureDirs();
            using Godot.FileAccess f = Godot.FileAccess.Open(
                TelemetryPaths.OptOutMarkerFile, Godot.FileAccess.ModeFlags.Write);
            if (f == null)
            {
                GD.PushWarning("[telemetry] could not write the usage opt-out marker ("
                               + Godot.FileAccess.GetOpenError()
                               + "); the opt-out is still recorded in settings.cfg.");
                return;
            }
            f.StoreString(
                "Anonymous usage reporting is OFF for this install.\n"
                + "This file's EXISTENCE is the setting; nothing is read out of it.\n"
                + "Turn reporting back on in Settings, which deletes this file.\n");
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[telemetry] could not write the usage opt-out marker: " + e.Message);
        }
    }

    private static void RemoveOptOutMarker()
    {
        if (!OptOutMarkerExists())
            return;
        Error err = DirAccess.RemoveAbsolute(
            ProjectSettings.GlobalizePath(TelemetryPaths.OptOutMarkerFile));
        if (err != Error.Ok)
            GD.PushWarning("[telemetry] could not remove the usage opt-out marker (" + err + ").");
    }

    // settings.cfg is shared with [display] and [onboarding]. Saving through a ConfigFile that
    // FAILED to parse would rewrite the file with only our section — destroying every other
    // system's settings. On a parse failure (anything but file-not-found) the unreadable file is
    // moved aside as a backup so all sections restart from defaults together, deliberately.
    private static ConfigFile LoadSharedConfig()
    {
        var cfg = new ConfigFile();
        Error err = cfg.Load(TelemetryPaths.SettingsFile);
        if (err != Error.Ok && err != Error.FileNotFound)
        {
            string abs = ProjectSettings.GlobalizePath(TelemetryPaths.SettingsFile);
            try
            {
                System.IO.File.Copy(abs, abs + ".corrupt", overwrite: true);
                System.IO.File.Delete(abs);
                GD.PushWarning($"[telemetry] settings.cfg unreadable ({err}); backed up to settings.cfg.corrupt and reset.");
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"[telemetry] settings.cfg unreadable ({err}) and backup failed: {e.Message}");
            }
            cfg = new ConfigFile(); // discard any half-parsed state
        }
        return cfg;
    }
}
