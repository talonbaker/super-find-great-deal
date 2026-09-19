using System;
using System.Security.Cryptography;
using System.Text;

namespace MpFoundation.Net;

/// <summary>
/// Human-typeable room codes. Six uppercase letters, no digits — drawn from an alphabet
/// that omits the visually ambiguous letters (I, L, O) so a code can be read aloud over voice
/// chat or typed off a phone screen without confusion. Letters-only (no numbers) by design,
/// which also keeps 0/O and 1/I/L collisions impossible rather than merely unlikely.
///
/// LENGTH: 23^6 ≈ 148 million. Widened from four letters (23^4 ≈ 280k) once the code stopped
/// being a directory label and became an actual join capability — the server admits only
/// joiners that present it (NetworkManager.ExpectedRoomCode). 280k is a guessable space for an
/// attacker who can attempt joins in bulk; 148M is not.
///
/// SCOPE NOTE — what this length does NOT buy on its own: it defends against ONLINE GUESSING of
/// a specific room. It is no defence against someone enumerating the Steam lobby directory,
/// because a scraper *reads* rather than guesses — Steam hands a non-member every metadata key
/// AND value for any lobby a search returns. That is what <see cref="DeriveDirectoryKey"/>
/// addresses: the code itself is never published, only a costly one-way function of it.
///
/// Ported from the retired matchmaking service (matchmaking/RoomCode.cs): codes are now minted
/// by the HOST'S OWN CLIENT and published as Steam Lobby metadata (see SteamLobby) instead of
/// being registered with an HTTP phonebook.
/// </summary>
public static class RoomCode
{
    public const int Length = 6;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>
    /// PBKDF2 iterations behind <see cref="DeriveDirectoryKey"/>. The published key is the only
    /// thing a scraper has to work from, so this number IS the defence: it sets the per-guess
    /// cost of sweeping the 23^6 ≈ 148M code space for one room. 600k is the OWASP floor for
    /// PBKDF2-HMAC-SHA256. Public so the unit suite can assert it never quietly drops.
    /// </summary>
    public const int DirectoryKeyIterations = 600_000;

    /// <summary>
    /// Salt for the directory key. Fixed rather than random because BOTH sides must derive the
    /// same value from the code alone — the joiner has no way to learn a random salt before it
    /// has found the lobby. Binding in the title tag and a scheme version keeps keys from
    /// colliding across titles sharing an App ID, and leaves room to rotate the scheme.
    /// </summary>
    private const string DirectoryKeySalt = "sail-roomkey-v1|" + NetProfile.GameTag;

    public static string Generate()
    {
        System.Span<char> chars = stackalloc char[Length];
        for (int i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>True if <paramref name="code"/> is a well-formed room code (already uppercased).</summary>
    public static bool IsValid(string? code)
    {
        if (code is null || code.Length != Length)
            return false;
        foreach (char c in code)
        {
            if (Alphabet.IndexOf(c) < 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The value published to the Steam lobby index in place of the room code — a deliberately
    /// expensive one-way function of it, as 32 lowercase hex characters.
    ///
    /// WHY THIS EXISTS. The lobby index is world-readable: <c>RequestLobbyList</c> returns 50
    /// lobbies per call, worldwide, with no documented rate limit, and a non-member receives
    /// every metadata key AND value. Publishing the code there put the join credential in the
    /// same record as the join address, so the handshake gate (NetworkManager.ExpectedRoomCode)
    /// could be satisfied by anyone who scraped the directory rather than being told the code.
    /// Publishing this instead leaves a scraper with an address it cannot authenticate to.
    ///
    /// Both sides derive independently — the key is never transmitted — so the joiner's
    /// exact-match string filter still resolves a room in one call with no enumeration.
    ///
    /// SCOPE NOTE — what this does and does not buy. It defeats BULK scraping, which is the
    /// threat the lobby index actually presents: recovering one room's code means sweeping
    /// 23^6 ≈ 148M candidates at <see cref="DirectoryKeyIterations"/> PBKDF2 iterations each,
    /// on the order of 1e14 SHA-256 compressions. It is weaker against an attacker willing to
    /// spend GPU-hours on ONE room they have singled out; PBKDF2 is not memory-hard, so that
    /// cost is roughly a couple of GPU-hours, against a code that only lives as long as the
    /// session. If a single targeted room ever needs to be safe for longer than an evening,
    /// the lever is a longer code or a memory-hard KDF, not a bigger iteration count.
    /// The index still reveals that N sessions exist, and their world and protocol.
    /// </summary>
    /// <exception cref="ArgumentException">The code is not well-formed, so no directory entry
    /// should be published for it at all.</exception>
    public static string DeriveDirectoryKey(string code)
    {
        string canonical = code is null ? "" : code.Trim().ToUpperInvariant();
        if (!IsValid(canonical))
            throw new ArgumentException("not a well-formed room code", nameof(code));

        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(canonical),
            Encoding.UTF8.GetBytes(DirectoryKeySalt),
            DirectoryKeyIterations,
            HashAlgorithmName.SHA256,
            outputLength: 16);
        return Convert.ToHexString(derived).ToLowerInvariant();
    }
}
