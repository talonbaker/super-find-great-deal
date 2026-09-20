using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// The single file-location seam for the telemetry module, so the CI self-test can redirect
/// every telemetry file to an isolated scratch root and wipe it, never touching the dev's
/// real user://settings.cfg or telemetry state. Production uses the defaults.
/// </summary>
public static class TelemetryPaths
{
    public static string Root { get; private set; } = "user://telemetry";
    public static string SettingsFile { get; private set; } = "user://settings.cfg";

    public static string MarkerFile => $"{Root}/session_active.marker";

    /// <summary><b>The durable half of the usage-report opt-out.</b> Deliberately NOT in
    /// settings.cfg, and deliberately not derived from it.
    ///
    /// <para>Talon, 2026-08-30, twice and in his own emphasis: <i>"Once they disable this setting,
    /// please do not reenable it on them restarting the game. That would be very rude…"</i> Usage
    /// reporting is now ON by default, which means the ABSENCE of stored state reads as consent —
    /// so anything that loses the stored state silently opts a declining player back in. That is
    /// not hypothetical here: <c>TelemetryStore.LoadSharedConfig</c> moves an unparseable
    /// settings.cfg aside and starts every section from defaults, which before this file was the
    /// exact path back to "on".</para>
    ///
    /// <para>An opt-out therefore writes a second, independent witness in a different file in a
    /// different directory. The marker's mere EXISTENCE is the fact — nothing is parsed out of it,
    /// so it cannot be corrupted into a false "on", only deleted. Deleting or corrupting
    /// settings.cfg no longer re-enables reporting; only wiping the whole profile (a new install)
    /// does, which is the one case where there is genuinely no player decision left on disk.</para></summary>
    public static string OptOutMarkerFile => $"{Root}/usage_optout.marker";
    public static string PendingCrashFile => $"{Root}/pending_crash.json";
    public static string QueueDir => $"{Root}/queue";

    /// <summary>Test seam: point all telemetry files at an isolated root + settings file.</summary>
    public static void ResetForTests(string root, string settingsFile)
    {
        Root = root;
        SettingsFile = settingsFile;
    }

    /// <summary>Ensures <see cref="Root"/> and <see cref="QueueDir"/> exist.</summary>
    public static void EnsureDirs()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(QueueDir));
    }

    /// <summary>Recursively deletes the telemetry root (test cleanup / fresh-state setup).</summary>
    public static void Wipe()
    {
        string abs = ProjectSettings.GlobalizePath(Root);
        RemoveDirRecursive(abs);
    }

    private static void RemoveDirRecursive(string abs)
    {
        if (!DirAccess.DirExistsAbsolute(abs))
            return;
        using var dir = DirAccess.Open(abs);
        if (dir == null)
            return;
        dir.ListDirBegin();
        for (string name = dir.GetNext(); name.Length > 0; name = dir.GetNext())
        {
            string child = $"{abs}/{name}";
            if (dir.CurrentIsDir())
                RemoveDirRecursive(child);
            else
                DirAccess.RemoveAbsolute(child);
        }
        dir.ListDirEnd();
        DirAccess.RemoveAbsolute(abs);
    }
}
