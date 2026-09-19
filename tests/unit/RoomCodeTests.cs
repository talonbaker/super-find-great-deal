using System;
using System.Collections.Generic;
using System.Diagnostics;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Room codes: six uppercase letters from a 23-letter alphabet that omits the
/// visually-ambiguous I, L, O (and all digits). Validates charset, length, the
/// generator/validator agreement, and that the generator actually spans its alphabet
/// (a crude entropy floor, so a stuck RNG or truncated alphabet is caught).
/// </summary>
public class RoomCodeTests
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";

    [Fact]
    public void Generate_IsAlwaysValid_AndCorrectShape()
    {
        for (int i = 0; i < 5000; i++)
        {
            string code = RoomCode.Generate();
            Assert.Equal(RoomCode.Length, code.Length);
            Assert.True(RoomCode.IsValid(code), $"generated code rejected by IsValid: {code}");
            foreach (char c in code)
                Assert.Contains(c, Alphabet);
        }
    }

    [Fact]
    public void Alphabet_ExcludesAmbiguousLettersAndDigits()
    {
        foreach (char bad in "ILO0123456789")
            Assert.DoesNotContain(bad, Alphabet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // Every case below is exactly Length characters unless it is deliberately testing length,
    // so a character-class rejection can never be masked by a length rejection. When the code
    // widened 4 -> 6 these were all still 4 chars: they kept passing, but for the wrong reason,
    // and the excluded-letter checks were proving nothing.
    [InlineData("ABCDE")]    // too short (5)
    [InlineData("ABCDEFG")]  // too long (7)
    [InlineData("ABCDEI")]   // excluded letter I
    [InlineData("ABCDEO")]   // excluded letter O
    [InlineData("ABCDEL")]   // excluded letter L
    [InlineData("ABCDE1")]   // digit
    [InlineData("abcdef")]   // lowercase (validator expects already-uppercased)
    [InlineData("AB CDE")]   // right length, embedded space
    [InlineData("A@CDEF")]   // symbol
    public void IsValid_RejectsMalformed(string? code) => Assert.False(RoomCode.IsValid(code));

    [Fact]
    public void IsValid_AcceptsEveryAlphabetLetterInEveryPosition()
    {
        foreach (char c in Alphabet)
        {
            string code = new string(c, RoomCode.Length);
            Assert.True(RoomCode.IsValid(code), $"rejected all-{c} code");
        }
    }

    [Fact]
    public void Generate_SpansAlphabet_EntropyFloor()
    {
        // Over many codes every alphabet letter should appear at least once, and codes
        // should not collapse to a tiny set. Both catch a broken/constant generator.
        var seenChars = new HashSet<char>();
        var seenCodes = new HashSet<string>();
        for (int i = 0; i < 20000; i++)
        {
            string code = RoomCode.Generate();
            seenCodes.Add(code);
            foreach (char c in code) seenChars.Add(c);
        }
        Assert.Equal(Alphabet.Length, seenChars.Count);         // every letter drawn
        Assert.True(seenCodes.Count > 5000, $"only {seenCodes.Count} distinct codes in 20000 draws");
    }

    // ---------------------------------------------------------------------------------
    // Directory key — the value actually published to the Steam lobby index.
    //
    // The lobby index is world-readable: a non-member who receives a lobby from
    // RequestLobbyList gets every metadata key AND value. Publishing the room code there
    // put the join credential next to the join address, which meant the handshake gate
    // (NetworkManager.ExpectedRoomCode) could be passed by anyone who read the directory
    // rather than being told the code. DeriveDirectoryKey is what goes in the index
    // instead: a deliberately expensive one-way function of the code, computed
    // independently by host and joiner so exact-match lookup still works with no
    // enumeration.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void DeriveDirectoryKey_IsDeterministic()
    {
        // The whole scheme rests on this: host and joiner never exchange the key, they
        // each compute it from the code. If it is not stable, join-by-code stops working.
        Assert.Equal(RoomCode.DeriveDirectoryKey("QMTKPB"), RoomCode.DeriveDirectoryKey("QMTKPB"));
    }

    [Fact]
    public void DeriveDirectoryKey_DiffersBetweenCodes()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 24; i++)
            Assert.True(seen.Add(RoomCode.DeriveDirectoryKey(RoomCode.Generate())), "duplicate directory key");
    }

    [Fact]
    public void DeriveDirectoryKey_NormalizesCase()
    {
        // JoinMenu / LaunchOptions uppercase before validating, but the derivation must not
        // depend on that: a lowercase code reaching this by any path has to resolve to the
        // same lobby, not silently fail to find a room that is right there.
        Assert.Equal(RoomCode.DeriveDirectoryKey("QMTKPB"), RoomCode.DeriveDirectoryKey("qmtkpb"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDE")]    // too short
    [InlineData("ABCDEFG")]  // too long
    [InlineData("ABCDEI")]   // excluded letter
    [InlineData("A@CDEF")]   // symbol
    public void DeriveDirectoryKey_RejectsMalformedCode(string? code)
    {
        // Fail loudly rather than publishing a directory entry for a code nobody can type.
        Assert.Throws<ArgumentException>(() => RoomCode.DeriveDirectoryKey(code!));
    }

    [Fact]
    public void DeriveDirectoryKey_NeverContainsThePlaintextCode()
    {
        // The negative control for the entire privacy property. If someone ever "optimises"
        // the derivation into an encoding, or prefixes the key with the code for debugging,
        // this is what catches it — the published value must not carry the credential.
        // Kept to a small sample on purpose: the property is structural, not statistical, and
        // each derivation deliberately costs ~45ms, so a big loop buys nothing but a slow suite.
        for (int i = 0; i < 32; i++)
        {
            string code = RoomCode.Generate();
            string key = RoomCode.DeriveDirectoryKey(code);
            Assert.DoesNotContain(code, key, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DeriveDirectoryKey_IsHexAndFixedWidth()
    {
        // Steam lobby metadata is a string KV store and the lookup is a string equality
        // filter, so the key must be plain, single-case, delimiter-free text.
        string key = RoomCode.DeriveDirectoryKey("QMTKPB");
        Assert.Equal(32, key.Length);
        foreach (char c in key)
            Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), $"non-hex char '{c}' in directory key");
    }

    [Fact]
    public void DeriveDirectoryKey_IterationCountResistsBulkInversion()
    {
        // 23^6 = 148M codes. The key is public, so the only thing standing between a scraper
        // and the credential is the per-guess cost of the KDF. At 600k PBKDF2-SHA256
        // iterations that is ~1e14 SHA-256 compressions to sweep one room's space.
        // Asserted as a constant so the guard cannot be silently weakened.
        Assert.True(RoomCode.DirectoryKeyIterations >= 600_000,
            $"iteration count dropped to {RoomCode.DirectoryKeyIterations}");
    }

    [Fact]
    public void DeriveDirectoryKey_ActuallyPaysTheIterationCost()
    {
        // The constant above proves intent; this proves it is wired to real work. A derivation
        // that ignored the iteration count returns in MICROSECONDS, so the gap being detected
        // spans three orders of magnitude and the floor can sit far below the measured cost
        // (~45ms on the 2026-07-31 dev machine, which has SHA hardware acceleration) without
        // losing its power. Deliberately not set just under that measurement: a floor with only
        // 2x headroom becomes a flake on faster hardware, and a flaky guard gets deleted.
        // The ceiling guards the join UX, which waits on this synchronously.
        var sw = Stopwatch.StartNew();
        RoomCode.DeriveDirectoryKey("QMTKPB");
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 5,
            $"derivation took only {sw.ElapsedMilliseconds}ms — the iteration count is not being applied");
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"derivation took {sw.ElapsedMilliseconds}ms — too slow to sit in the join path");
    }
}
