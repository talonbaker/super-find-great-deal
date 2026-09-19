namespace MpFoundation.Telemetry;

/// <summary>
/// Checked-in Firebase Firestore credentials for the telemetry backend. The Web API key is
/// PUBLIC BY DESIGN — it grants nothing beyond what the Firestore security rules allow
/// (create-only on the `events` collection; see docs/telemetry-setup.md). It is not a
/// secret and is meant to live in source.
/// </summary>
public static class TelemetryConfig
{
    /// <summary>Firebase project ID.</summary>
    public const string ProjectId = "sail-dd3e6";

    /// <summary>Firebase Web API key (public; protected by security rules).</summary>
    public const string ApiKey = "AIzaSyC600BHtoXw-Z1b27cAyewa7fV8iqY2-MA";

    /// <summary>Build version stamped onto every report. Derives from the single source of
    /// truth in <see cref="MpFoundation.BuildInfo.Version"/> so telemetry can never disagree
    /// with the shipped binary's version.</summary>
    public const string BuildVersion = BuildInfo.Version;

    /// <summary>True only when both credentials are present. The single gate the autoload
    /// checks before doing anything player-facing or network-facing.</summary>
    public static bool IsConfigured => ProjectId.Length > 0 && ApiKey.Length > 0;
}
