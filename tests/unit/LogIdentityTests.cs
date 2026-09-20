using System;
using System.Collections.Generic;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Log-facing peer identities. The host's server log accumulates one line per connect,
/// rejection and disconnect, each carrying whatever <c>SteamPeer.IdentityOf</c> /
/// <c>ENetPacketPeer.GetRemoteAddress</c> returned — a SteamID64 or a real IP belonging to
/// someone who never consented to anything on the host's machine. Those files rotate by size
/// only, never by age, and survive uninstall.
///
/// <see cref="LogIdentity"/> replaces the raw value with a per-session token: stable enough to
/// follow one peer through a session's worth of lines, useless for identifying them afterwards.
/// </summary>
public class LogIdentityTests
{
    private static readonly byte[] SaltA = new byte[32];
    private static readonly byte[] SaltB = Fill(32, 0xAB);

    private static byte[] Fill(int n, byte v)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = v;
        return b;
    }

    [Fact]
    public void SamePeer_WithinASession_GetsTheSameToken()
    {
        // The whole operational value of the token: an operator diagnosing an abusive peer can
        // still see that the connect, the rejection and the retry were all the same party.
        Assert.Equal(
            LogIdentity.Compute(SaltA, "steam:76561198012345678"),
            LogIdentity.Compute(SaltA, "steam:76561198012345678"));
    }

    [Fact]
    public void DifferentPeers_GetDifferentTokens()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 500; i++)
            Assert.True(seen.Add(LogIdentity.Compute(SaltA, $"192.0.2.{i / 250}:{5000 + i}")),
                "two distinct peers collided onto one token");
    }

    [Fact]
    public void SamePeer_AcrossSessions_GetsDifferentTokens()
    {
        // The property that makes yesterday's log useless: the salt is fresh per process and
        // never persisted, so tokens cannot be correlated across sessions or against a
        // precomputed table of every SteamID64 in existence.
        Assert.NotEqual(
            LogIdentity.Compute(SaltA, "steam:76561198012345678"),
            LogIdentity.Compute(SaltB, "steam:76561198012345678"));
    }

    [Theory]
    [InlineData("steam:76561198012345678", "76561198012345678")]
    [InlineData("203.0.113.44:57314", "203.0.113.44")]
    [InlineData("jennifer.okafor", "jennifer.okafor")]
    public void Token_NeverContainsTheRawIdentity(string raw, string secret)
    {
        // The negative control for the privacy property. Catches anyone who later "improves"
        // this by prefixing the token with the address for readability.
        string token = LogIdentity.Compute(SaltA, raw);
        Assert.DoesNotContain(secret, token, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Token_IsShortFixedWidthHex()
    {
        // Log lines are read by humans and grepped by the test harness; the token has to be
        // compact, single-case and free of the delimiters the kv format uses.
        string token = LogIdentity.Compute(SaltA, "203.0.113.44:57314");
        Assert.Equal(8, token.Length);
        foreach (char c in token)
            Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), $"non-hex char '{c}'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AbsentIdentity_ReadsAsAbsent_NotAsAToken(string? raw)
    {
        // Hashing "" would mint a plausible-looking token for "we had no address", which reads
        // in a log exactly like a real peer. Say nothing instead.
        Assert.Equal("none", LogIdentity.Compute(SaltA, raw!));
    }

    [Fact]
    public void SessionToken_UsesTheProcessSalt_AndIsStable()
    {
        // The call sites use this one-argument form; it must agree with itself.
        Assert.Equal(LogIdentity.Of("steam:76561198012345678"), LogIdentity.Of("steam:76561198012345678"));
        Assert.NotEqual(LogIdentity.Of("steam:76561198012345678"), LogIdentity.Of("steam:76561198099999999"));
        Assert.DoesNotContain("76561198012345678", LogIdentity.Of("steam:76561198012345678"));
    }
}
