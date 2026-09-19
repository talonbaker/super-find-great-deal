using System;
using System.Security.Cryptography;
using System.Text;

namespace MpFoundation.Net;

/// <summary>
/// Turns a peer's real network identity into a token that is safe to write to a log file.
///
/// WHY. When a player hosts, their machine becomes the server, and <see cref="ServerLog"/>
/// records a line for every connect, rejection, disconnect and desync — each carrying whatever
/// the transport reported as the peer's address: a SteamID64 on the Steam relay, a real IP on
/// ENet/LAN. Those are other people's identifiers, being written to a third party's disk by a
/// game none of them installed as a server. The files rotate by SIZE only, never by age, and
/// they outlive an uninstall. Nobody consented to that and nothing ever cleans it up.
///
/// WHAT IT PRESERVES. The token is stable for the life of the process, so a host or an operator
/// reading the log can still see that the rejected connect, the retry and the eventual join were
/// all the same party — which is the entire diagnostic value those lines ever had. What it drops
/// is the ability to say *who* that party was, then or ever afterwards.
///
/// WHY THE SALT IS PER-PROCESS AND NEVER PERSISTED. A bare hash of a SteamID64 is not an
/// anonymization: the space is small and public, so anyone holding the log could rebuild the
/// mapping with a table. A random per-session salt makes the tokens unlinkable both to the real
/// identity and across sessions — last week's log cannot be joined to this week's.
///
/// SCOPE NOTE. This protects the log. The rate limiter and the auth bookkeeping still hold the
/// raw values IN MEMORY, which they must in order to work; they are simply never written down.
/// This also does nothing about identities the transport hands to other subsystems — if a new
/// one starts logging an address, it needs this call too.
/// </summary>
public static class LogIdentity
{
    /// <summary>Marker for "there was no address", kept distinguishable from a real token so an
    /// empty identity cannot read in a log as a peer that was actually there.</summary>
    public const string Absent = "none";

    private const int TokenChars = 8;

    /// <summary>
    /// Fresh for every process, never written to disk, never sent anywhere. Regenerating it is
    /// what expires every token in every log this machine has ever written.
    /// </summary>
    private static readonly byte[] SessionSalt = RandomNumberGenerator.GetBytes(32);

    /// <summary>The log-safe token for a peer identity, using this process's session salt.
    /// This is the form call sites use.</summary>
    public static string Of(string? rawIdentity) => Compute(SessionSalt, rawIdentity);

    /// <summary>
    /// Salt-explicit form. Exists so the unit suite can prove the properties that matter —
    /// stability under one salt, and non-correlation across two — without reaching into process
    /// state or needing a way to re-roll it.
    /// </summary>
    public static string Compute(byte[] salt, string? rawIdentity)
    {
        if (string.IsNullOrEmpty(rawIdentity))
            return Absent;
        byte[] mac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(rawIdentity));
        return Convert.ToHexString(mac, 0, TokenChars / 2).ToLowerInvariant();
    }
}
