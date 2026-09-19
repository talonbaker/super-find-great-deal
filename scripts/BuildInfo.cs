namespace MpFoundation;

/// <summary>
/// Single source of truth for the build/release version. Date-based CalVer
/// <c>YYYY.MM.DD</c> (e.g. <c>2026.07.16</c>); a second build on the same day takes a
/// <c>-N</c> suffix (<c>2026.07.16-2</c>). Everything else derives from this constant —
/// telemetry's reported build version (<see cref="Telemetry.TelemetryConfig.BuildVersion"/>),
/// the Windows export's file/product version in <c>export_presets.cfg</c> (stamped at export
/// time by <c>deploy/Export-WindowsClient.ps1</c>), and the git tag written by
/// <c>deploy/steam/Upload-Steam.ps1</c> on a successful upload — so the client, the telemetry
/// backend, and the shipped binary's metadata can never drift apart.
/// </summary>
public static class BuildInfo
{
    public const string Version = "2026.08.30";
}
