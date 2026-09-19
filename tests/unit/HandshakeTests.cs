using System;
using System.Text;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The version-handshake text format. Round-trip both hellos, and hold the parsers to
/// their promise ("Returns false, never throws, on anything malformed") against oversized,
/// non-ASCII, and structurally-broken payloads plus a deterministic random fuzz loop.
/// </summary>
public class HandshakeTests
{
    private const int FuzzIterations = 100_000;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void ClientHello_RoundTrips(int protocol)
    {
        byte[] data = Handshake.ClientHello(protocol);
        Assert.True(Handshake.TryParseClient(data, out Handshake.ClientInfo info));
        Assert.Equal(protocol, info.Protocol);
    }

    [Theory]
    [InlineData(0, Handshake.ServerStatus.Ok)]
    [InlineData(7, Handshake.ServerStatus.Full)]
    [InlineData(int.MaxValue, Handshake.ServerStatus.Ok)]
    public void ServerHello_RoundTrips(int protocol, Handshake.ServerStatus status)
    {
        byte[] data = Handshake.ServerHello(protocol, status);
        Assert.True(Handshake.TryParseServer(data, out Handshake.ServerInfo info));
        Assert.Equal(protocol, info.Protocol);
        Assert.Equal(status, info.Status);
    }

    [Fact]
    public void ClientHello_IsAscii_AndWithinMaxPayload()
    {
        byte[] data = Handshake.ClientHello(123);
        Assert.True(data.Length <= Handshake.MaxPayloadBytes);
        foreach (byte b in data)
            Assert.InRange(b, (byte)0x20, (byte)0x7E);
    }

    // --- Rejection --------------------------------------------------------------------

    [Fact]
    public void TryParseClient_Empty_ReturnsFalse() =>
        Assert.False(Handshake.TryParseClient(Array.Empty<byte>(), out _));

    [Fact]
    public void TryParseClient_Oversized_ReturnsFalse()
    {
        var data = new byte[Handshake.MaxPayloadBytes + 1];
        Array.Fill(data, (byte)'A');
        Assert.False(Handshake.TryParseClient(data, out _));
    }

    [Theory]
    [InlineData("MPF;S;1")]        // server shape fed to client parser
    [InlineData("MPF;C")]          // too few parts
    [InlineData("MPF;C;1;extra")]  // too many parts
    [InlineData("XXX;C;1")]        // bad magic
    [InlineData("MPF;X;1")]        // bad role
    [InlineData("MPF;C;")]         // empty protocol
    [InlineData("MPF;C;-1")]       // sign not allowed (NumberStyles.None)
    [InlineData("MPF;C;+1")]
    [InlineData("MPF;C; 1")]       // whitespace not allowed
    [InlineData("MPF;C;1.0")]      // non-integer
    [InlineData("MPF;C;99999999999999999999")] // overflow
    public void TryParseClient_Malformed_ReturnsFalse(string payload) =>
        Assert.False(Handshake.TryParseClient(Encoding.ASCII.GetBytes(payload), out _));

    [Theory]
    [InlineData("MPF;C;1")]        // client shape fed to server parser
    [InlineData("MPF;S;1")]        // missing status
    [InlineData("MPF;S;1;MAYBE")]  // unknown status
    [InlineData("MPF;S;1;ok")]     // wrong case
    [InlineData("MPF;S;1;OK;x")]   // too many parts
    public void TryParseServer_Malformed_ReturnsFalse(string payload) =>
        Assert.False(Handshake.TryParseServer(Encoding.ASCII.GetBytes(payload), out _));

    [Fact]
    public void TryParse_NonAscii_ReturnsFalse()
    {
        // A valid-looking client hello with a non-ASCII byte spliced in must be rejected
        // by the pre-decode byte scan, not decoded lossily.
        byte[] good = Handshake.ClientHello(1);
        var bad = new byte[good.Length];
        Array.Copy(good, bad, good.Length);
        bad[^1] = 0xC3; // start of a UTF-8 multibyte sequence; > 0x7E
        Assert.False(Handshake.TryParseClient(bad, out _));
    }

    [Fact]
    public void TryParse_ControlBytes_ReturnsFalse()
    {
        byte[] data = Encoding.ASCII.GetBytes("MPF;C;1");
        data[3] = 0x00; // NUL in place of ';' — control byte < 0x20
        Assert.False(Handshake.TryParseClient(data, out _));
    }

    // --- Deterministic random fuzz ----------------------------------------------------

    [Fact]
    public void Fuzz_Parsers_NeverThrow_OnRandomBytes()
    {
        var rng = new Random(0x48414E44); // fixed seed (deterministic)
        for (int i = 0; i < FuzzIterations; i++)
        {
            int len = rng.Next(0, Handshake.MaxPayloadBytes + 8);
            var buf = new byte[len];
            rng.NextBytes(buf);

            // Must not throw; return value is whatever — we only assert liveness/safety.
            bool c = Handshake.TryParseClient(buf, out _);
            bool s = Handshake.TryParseServer(buf, out _);

            // A payload can never satisfy both the 3-part client and 4-part server shape.
            Assert.False(c && s);
        }
    }

    [Fact]
    public void Fuzz_RandomAsciiSemicolonStrings_NeverThrow()
    {
        // Bias toward the grammar: random ASCII tokens joined by ';' exercise the split /
        // magic / role / int-parse branches far more than uniform random bytes do.
        var rng = new Random(20260731);
        string[] toks = { "MPF", "C", "S", "OK", "FULL", "1", "42", "", "x", "-1", "999", "MPFX" };
        for (int i = 0; i < FuzzIterations; i++)
        {
            int parts = rng.Next(1, 6);
            var sb = new StringBuilder();
            for (int p = 0; p < parts; p++)
            {
                if (p > 0) sb.Append(';');
                sb.Append(toks[rng.Next(toks.Length)]);
            }
            byte[] buf = Encoding.ASCII.GetBytes(sb.ToString());
            Handshake.TryParseClient(buf, out _);
            Handshake.TryParseServer(buf, out _);
        }
    }
}
