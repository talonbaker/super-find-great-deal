using System.Globalization;

namespace MpFoundation.Net.Steam;

/// <summary>
/// The "steam:&lt;steamid64&gt;" address convention. Matchmaking is a phonebook of opaque
/// address strings; this prefix is how a registered address says "reach me through Steam
/// relay P2P, not a raw socket". Clients branch on it (Gameplay), the dedicated server
/// formats it at registration time, and the matchmaking service merely validates and
/// stores it — the service never needs Steam itself.
/// </summary>
public static class SteamAddress
{
    public const string Prefix = "steam:";

    public static string Format(ulong steamId64) => Prefix + steamId64.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses "steam:&lt;steamid64&gt;". Rejects empty, zero, non-digit, and
    /// overlong ids — a malformed matchmaking payload must fail here, not inside the
    /// Steam API with a nonsense identity.</summary>
    public static bool TryParse(string? address, out ulong steamId64)
    {
        steamId64 = 0;
        if (string.IsNullOrWhiteSpace(address))
            return false;
        string trimmed = address.Trim();
        if (!trimmed.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
            return false;

        string digits = trimmed[Prefix.Length..];
        // A SteamID64 is at most 20 digits (ulong.MaxValue); anything longer is garbage.
        if (digits.Length is 0 or > 20)
            return false;
        foreach (char c in digits)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out steamId64)
            && steamId64 != 0;
    }

    public static bool IsSteamAddress(string? address) => TryParse(address, out _);
}
