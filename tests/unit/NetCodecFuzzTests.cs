using System;
using System.Collections.Generic;
using MpFoundation.Game.Aim;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Adversarial and randomized input for the server-facing parsers. The contract
/// (NetCodec class doc): "a malformed packet from a hostile client yields null, never an
/// exception or a poisoned simulation." These tests hold the parsers to it — they must
/// never throw, never over-allocate, and reject anything that is not exactly well-formed.
///
/// The random loop uses a FIXED SEED so a failure is reproducible and CI is stable; no
/// external fuzzing dependency is needed.
///
/// Byte counts below are read off NetCodec.InputEntryBytes / NetCodec.SnapshotBytes rather than
/// written as literals — the WP-L3 aim substrate, the SKID-1 skid float and MOVE-3's second
/// buttons byte have each moved one of them, and a literal is a line somebody has to remember to
/// update. See NetCodec's InputEntryBytes/SnapshotBytes comments for the layout.
/// </summary>
public class NetCodecFuzzTests
{
    private const int FuzzIterations = 100_000;
    private const int Seed = 0x5A11_C0DE;

    // --- UnpackInputs: targeted rejection --------------------------------------------

    [Fact]
    public void UnpackInputs_Null_ReturnsNull() => Assert.Null(NetCodec.UnpackInputs(null));

    [Fact]
    public void UnpackInputs_Empty_ReturnsNull() => Assert.Null(NetCodec.UnpackInputs(Array.Empty<byte>()));

    [Fact]
    public void UnpackInputs_CountZero_ReturnsNull() => Assert.Null(NetCodec.UnpackInputs(new byte[] { 0 }));

    [Fact]
    public void UnpackInputs_CountByte255_ShortBuffer_ReturnsNullWithoutAllocating()
    {
        // count=255 would demand 1+255*InputEntryBytes; a 1-byte buffer must be rejected on the
        // length check BEFORE any per-entry list is grown. (No exception, no big alloc.)
        Assert.Null(NetCodec.UnpackInputs(new byte[] { 255 }));
    }

    [Fact]
    public void UnpackInputs_CountAboveMax_ExactLength_ReturnsNull()
    {
        int over = NetCodec.MaxInputEntries + 1; // 9
        var buf = new byte[1 + over * NetCodec.InputEntryBytes];
        buf[0] = (byte)over;
        Assert.Null(NetCodec.UnpackInputs(buf));
    }

    [Fact]
    public void UnpackInputs_TruncatedByOneByte_ReturnsNull()
    {
        var buf = new byte[1 + 1 * NetCodec.InputEntryBytes - 1]; // one byte short of a single entry
        buf[0] = 1;
        Assert.Null(NetCodec.UnpackInputs(buf));
    }

    [Fact]
    public void UnpackInputs_OneExtraByte_ReturnsNull()
    {
        var buf = new byte[1 + 1 * NetCodec.InputEntryBytes + 1]; // one byte too many
        buf[0] = 1;
        Assert.Null(NetCodec.UnpackInputs(buf));
    }

    [Fact]
    public void UnpackInputs_AllOxFF_ReturnsNull()
    {
        // First byte 0xFF = 255 > MaxInputEntries and length won't match: rejected.
        var buf = new byte[64];
        Array.Fill(buf, (byte)0xFF);
        Assert.Null(NetCodec.UnpackInputs(buf));
    }

    [Fact]
    public void UnpackInputs_GarbageFloats_ScrubbedNotThrown()
    {
        // A well-formed length with NaN/Inf floats must parse (never throw) and be sanitized:
        // MoveDir becomes finite/unit-disc, SpeedFactor is clamped into range, AimYaw becomes
        // finite, AimPitch is finite AND clamped into SandboxCamera.PitchMin/Max.
        var buf = new byte[1 + NetCodec.InputEntryBytes];
        buf[0] = 1;
        // seq bytes 1..4 arbitrary; dirX@5..8, dirZ@9..12, buttons@13, buttons2@14, sf@15..18,
        // aimYaw@19..22, aimPitch@23..26 (see NetCodec's InputEntryBytes layout comment). MOVE-3
        // inserted buttons2 after buttons, so every offset from the speed factor on moved by one.
        BitConverter.GetBytes(float.NaN).CopyTo(buf, 5);
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(buf, 9);
        buf[13] = 0xFF; // all buttons set, including FlagAimRaise (extra bits ignored)
        buf[14] = 0xFF; // buttons2: JumpHeld set, the seven free bits ignored (MOVE-3)
        BitConverter.GetBytes(float.NegativeInfinity).CopyTo(buf, 15);
        BitConverter.GetBytes(float.NaN).CopyTo(buf, 19);
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(buf, 23);

        var got = NetCodec.UnpackInputs(buf);

        Assert.NotNull(got);
        var e = got![0];
        Assert.True(float.IsFinite(e.Intent.MoveDir.X));
        Assert.True(float.IsFinite(e.Intent.MoveDir.Z));
        Assert.Equal(0f, e.Intent.MoveDir.Y); // contract: flat direction
        Assert.True(e.Intent.MoveDir.Length() <= 1.0001f);
        Assert.InRange(e.SpeedFactor, 0.55f, 1f);
        Assert.True(e.Intent.AimRaise);
        Assert.True(float.IsFinite(e.Intent.AimYaw));
        Assert.True(float.IsFinite(e.Intent.AimPitch));
        Assert.InRange(e.Intent.AimPitch, SandboxCamera.PitchMin, SandboxCamera.PitchMax);
    }

    // --- UnpackSnapshot: targeted rejection -------------------------------------------

    [Fact]
    public void UnpackSnapshot_Null_ReturnsNull() => Assert.Null(NetCodec.UnpackSnapshot(null));

    [Fact]
    public void UnpackSnapshot_Empty_ReturnsNull() => Assert.Null(NetCodec.UnpackSnapshot(Array.Empty<byte>()));

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(54)]
    [InlineData(56)]
    [InlineData(51)]   // the pre-SKID-1 length: a v10 peer's snapshot must be REFUSED, not misread
    [InlineData(55 * 2)]
    public void UnpackSnapshot_WrongLength_ReturnsNull(int len) =>
        Assert.Null(NetCodec.UnpackSnapshot(new byte[len]));

    [Fact]
    public void UnpackSnapshot_AllZero_ParsesToZeroState()
    {
        // A buffer of zero bytes is all-finite zeros and AimStance byte 0 == AimStance.Lowered (a
        // legal ordinal): a legal (if boring) snapshot, not a reject.
        var got = NetCodec.UnpackSnapshot(new byte[NetCodec.SnapshotBytes]);
        Assert.NotNull(got);
        Assert.Equal(0u, got!.Value.Tick);
        Assert.Equal(Godot.Vector3.Zero, got.Value.State.Position);
        Assert.Equal(AimStance.Lowered, got.Value.AimStance);
        Assert.Equal(0f, got.Value.AimSteadyElapsedSec);
    }

    [Theory]
    [InlineData(14)]  // Position.X
    [InlineData(18)]  // Position.Y
    [InlineData(22)]  // Position.Z
    [InlineData(26)]  // Velocity.X
    [InlineData(38)]  // Yaw
    [InlineData(42)]  // CoyoteRemaining
    [InlineData(47)]  // AimSteadyElapsedSec (WP-L3)
    [InlineData(51)]  // SkidRemaining (SKID-1)
    public void UnpackSnapshot_NonFiniteFloatAtOffset_ReturnsNull(int offset)
    {
        var buf = new byte[NetCodec.SnapshotBytes];
        BitConverter.GetBytes(float.NaN).CopyTo(buf, offset);
        Assert.Null(NetCodec.UnpackSnapshot(buf));

        var buf2 = new byte[NetCodec.SnapshotBytes];
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(buf2, offset);
        Assert.Null(NetCodec.UnpackSnapshot(buf2));
    }

    [Theory]
    [InlineData(4)]   // AimStance.Lowering is 3 — the first invalid ordinal
    [InlineData(5)]
    [InlineData(255)]
    public void UnpackSnapshot_InvalidAimStanceOrdinal_ReturnsNull(byte aimStanceByte)
    {
        var buf = new byte[NetCodec.SnapshotBytes];
        buf[46] = aimStanceByte;
        Assert.Null(NetCodec.UnpackSnapshot(buf));
    }

    [Theory]
    [InlineData((byte)0)] // Lowered
    [InlineData((byte)1)] // Raising
    [InlineData((byte)2)] // Raised
    [InlineData((byte)3)] // Lowering
    public void UnpackSnapshot_EveryValidAimStanceOrdinal_Accepted(byte aimStanceByte)
    {
        var buf = new byte[NetCodec.SnapshotBytes];
        buf[46] = aimStanceByte;
        var got = NetCodec.UnpackSnapshot(buf);
        Assert.NotNull(got);
        Assert.Equal((AimStance)aimStanceByte, got!.Value.AimStance);
    }

    // --- Deterministic random fuzz ----------------------------------------------------

    [Fact]
    public void Fuzz_UnpackInputs_NeverThrows_AndRespectsLengthContract()
    {
        var rng = new Random(Seed);
        var scratch = new byte[1 + NetCodec.MaxInputEntries * NetCodec.InputEntryBytes + 8];
        for (int i = 0; i < FuzzIterations; i++)
        {
            int len = rng.Next(0, scratch.Length + 1);
            var buf = new byte[len];
            rng.NextBytes(buf);

            List<NetCodec.InputEntry>? got = NetCodec.UnpackInputs(buf);

            if (got != null)
            {
                // Only shape that can succeed: count in [1,Max] and exact length.
                int count = buf[0];
                Assert.InRange(count, 1, NetCodec.MaxInputEntries);
                Assert.Equal(1 + count * NetCodec.InputEntryBytes, buf.Length);
                Assert.Equal(count, got.Count);
                // Never over-allocated: bounded by MaxInputEntries.
                Assert.True(got.Count <= NetCodec.MaxInputEntries);
            }
        }
    }

    [Fact]
    public void Fuzz_UnpackSnapshot_NeverThrows_AndOnlyAcceptsFiniteExactLength()
    {
        var rng = new Random(Seed ^ 0x1234);
        for (int i = 0; i < FuzzIterations; i++)
        {
            // Bias toward the accept-eligible length so the finiteness path is exercised.
            int len = (i % 3 == 0) ? NetCodec.SnapshotBytes : rng.Next(0, 110);
            var buf = new byte[len];
            rng.NextBytes(buf);

            NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(buf);

            if (got != null)
            {
                Assert.Equal(NetCodec.SnapshotBytes, buf.Length);
                NetCodec.Snapshot s = got.Value;
                Assert.True(float.IsFinite(s.State.Position.X) && float.IsFinite(s.State.Position.Y)
                    && float.IsFinite(s.State.Position.Z));
                Assert.True(float.IsFinite(s.State.Velocity.X) && float.IsFinite(s.State.Velocity.Y)
                    && float.IsFinite(s.State.Velocity.Z));
                Assert.True(float.IsFinite(s.State.Yaw));
                Assert.True(float.IsFinite(s.State.CoyoteRemaining));
                Assert.True(float.IsFinite(s.State.JumpBufferRemaining));
                Assert.True(float.IsFinite(s.AimSteadyElapsedSec));
                Assert.InRange((byte)s.AimStance, (byte)AimStance.Lowered, (byte)AimStance.Lowering);
            }
        }
    }

    [Fact]
    public void Fuzz_RoundTripsSurviveRandomValidSnapshots()
    {
        // Pack random-but-finite snapshots and require exact recovery: fuzzing the encoder,
        // not just the rejecter.
        var rng = new Random(Seed ^ 0x7777);
        var stances = new[] { AimStance.Lowered, AimStance.Raising, AimStance.Raised, AimStance.Lowering };
        for (int i = 0; i < 20_000; i++)
        {
            var snap = new NetCodec.Snapshot(
                (uint)rng.NextInt64(), (byte)rng.Next(256), (uint)rng.NextInt64(),
                new MoveState
                {
                    Position = RandFiniteVec(rng),
                    Velocity = RandFiniteVec(rng),
                    Yaw = RandFinite(rng),
                    CoyoteRemaining = RandFinite(rng),
                    JumpBufferRemaining = RandFinite(rng),
                    Grounded = rng.Next(2) == 0,
                },
                stances[rng.Next(stances.Length)],
                RandFinite(rng));

            var got = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));

            Assert.NotNull(got);
            var g = got!.Value;
            Assert.Equal(snap.Tick, g.Tick);
            Assert.Equal(snap.Epoch, g.Epoch);
            Assert.Equal(snap.LastProcessedSeq, g.LastProcessedSeq);
            Assert.Equal(snap.State.Position, g.State.Position);
            Assert.Equal(snap.State.Velocity, g.State.Velocity);
            Assert.Equal(snap.State.Yaw, g.State.Yaw);
            Assert.Equal(snap.State.Grounded, g.State.Grounded);
            Assert.Equal(snap.AimStance, g.AimStance);
            Assert.Equal(snap.AimSteadyElapsedSec, g.AimSteadyElapsedSec);
        }
    }

    private static float RandFinite(Random rng) => (float)(rng.NextDouble() * 2000.0 - 1000.0);
    private static Godot.Vector3 RandFiniteVec(Random rng) =>
        new(RandFinite(rng), RandFinite(rng), RandFinite(rng));
}
