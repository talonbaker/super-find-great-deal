using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Aim;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Byte-exact round-trip discipline for the two movement wire types. Pack then unpack and
/// assert every field survives. Values are pre-sanitized (finite, MoveDir on the unit disc
/// with Y=0, SpeedFactor in [0.55,1], AimPitch within SandboxCamera.PitchMin/Max) so
/// <see cref="NetCodec.UnpackInputs"/>'s built-in scrubbing (AvatarMotor.SanitizeMoveDir /
/// SanitizeSpeedFactor / SanitizeAimYaw / SanitizeAimPitch) is the identity here and the
/// comparison is exact rather than approximate. AimRaise/AimYaw/AimPitch and
/// AimStance/AimSteadyElapsedSec are WP-L3 additions, appended after the pre-existing wire
/// fields (see NetCodec's own InputEntryBytes/SnapshotBytes comments) — every original test
/// below is otherwise unchanged, just re-pointed at the new byte counts.
/// </summary>
public class NetCodecRoundTripTests
{
    private static NetCodec.InputEntry Entry(
        uint seq, float x, float z, bool jump, bool interact, bool @throw, bool sprint, float sf,
        bool aimRaise = false, float aimYaw = 0f, float aimPitch = 0f, bool jumpHeld = false)
        => new(
            new MoveIntent
            {
                MoveDir = new Vector3(x, 0, z),
                Jump = jump,
                Interact = interact,
                Throw = @throw,
                Sprint = sprint,
                JumpHeld = jumpHeld,
                AimRaise = aimRaise,
                AimYaw = aimYaw,
                AimPitch = aimPitch,
                Seq = seq,
            },
            sf);

    [Fact]
    public void Inputs_SingleEntry_RoundTripsEveryField()
    {
        var entries = new List<NetCodec.InputEntry>
        {
            Entry(0xDEADBEEF, 0.5f, -0.25f, jump: true, interact: false, @throw: true, sprint: false, sf: 0.8f,
                aimRaise: true, aimYaw: 2.3f, aimPitch: -0.4f, jumpHeld: true),
        };

        byte[] packet = NetCodec.PackInputs(entries);
        List<NetCodec.InputEntry>? got = NetCodec.UnpackInputs(packet);

        Assert.NotNull(got);
        Assert.Single(got!);
        AssertEntryEqual(entries[0], got![0]);
    }

    [Fact]
    public void Inputs_AllButtonCombinations_RoundTrip()
    {
        // 64 combinations of the six edge/level flags (AimRaise, and MOVE-3's JumpHeld — the
        // first one living in the SECOND buttons byte) — proves no flag bit collides, and that
        // the two bytes do not shadow each other.
        for (int mask = 0; mask < 64; mask++)
        {
            // Keep the direction on the unit disc (|dir| <= 1) even at mask=127 so UnpackInputs's
            // SanitizeMoveDir is the identity and the comparison stays byte-exact.
            var e = Entry(
                seq: (uint)(mask * 7 + 1),
                x: 0.005f * mask, z: -0.00125f * mask,
                jump: (mask & 1) != 0,
                interact: (mask & 2) != 0,
                @throw: (mask & 4) != 0,
                sprint: (mask & 8) != 0,
                sf: 0.6f,
                aimRaise: (mask & 16) != 0,
                jumpHeld: (mask & 32) != 0);
            var list = new List<NetCodec.InputEntry> { e };

            var got = NetCodec.UnpackInputs(NetCodec.PackInputs(list));

            Assert.NotNull(got);
            AssertEntryEqual(e, got![0]);
        }
    }

    [Fact]
    public void Inputs_FullRedundancyWindow_RoundTrips()
    {
        var entries = new List<NetCodec.InputEntry>();
        for (int i = 0; i < NetCodec.MaxInputEntries; i++)
            // Direction kept on the unit disc (see note above) for exact round-trip.
            entries.Add(Entry((uint)(1000 + i), 0.08f * i - 0.3f, 0.2f, i % 2 == 0, false, false, i % 3 == 0, 0.75f,
                aimRaise: i % 4 == 0, aimYaw: 0.1f * i, aimPitch: 0f));

        var got = NetCodec.UnpackInputs(NetCodec.PackInputs(entries));

        Assert.NotNull(got);
        Assert.Equal(entries.Count, got!.Count);
        for (int i = 0; i < entries.Count; i++)
            AssertEntryEqual(entries[i], got[i]);
    }

    [Fact]
    public void Inputs_PackedLength_IsOnePlusInputEntryBytesPerEntry()
    {
        for (int n = 1; n <= NetCodec.MaxInputEntries; n++)
        {
            var entries = new List<NetCodec.InputEntry>();
            for (int i = 0; i < n; i++)
                entries.Add(Entry((uint)i + 1, 0, 0, false, false, false, false, 1f));
            byte[] packet = NetCodec.PackInputs(entries);
            Assert.Equal(1 + n * NetCodec.InputEntryBytes, packet.Length);
        }
    }

    [Theory]
    [InlineData(-1.05f)] // SandboxCamera.PitchMin — exact boundary
    [InlineData(1.05f)]  // SandboxCamera.PitchMax — exact boundary (raised from 0.30 by CATCH-1)
    [InlineData(0.30f)]  // the old PitchMax: still well inside the range, and must stay legal
    [InlineData(0f)]
    public void Inputs_AimPitchWithinLegalRange_RoundTripsExactly(float pitch)
    {
        var entries = new List<NetCodec.InputEntry> { Entry(1, 0, 0, false, false, false, false, 1f,
            aimRaise: true, aimYaw: -3.0f, aimPitch: pitch) };
        var got = NetCodec.UnpackInputs(NetCodec.PackInputs(entries));
        Assert.NotNull(got);
        Assert.Equal(pitch, got![0].Intent.AimPitch);
        Assert.Equal(-3.0f, got[0].Intent.AimYaw);
        Assert.True(got[0].Intent.AimRaise);
    }

    [Fact]
    public void Snapshot_RoundTripsEveryField()
    {
        var snap = new NetCodec.Snapshot(
            Tick: 0x01020304u,
            Epoch: 0xAB,
            LastProcessedSeq: 0x0A0B0C0Du,
            State: new MoveState
            {
                Position = new Vector3(12.5f, -3.25f, 7.75f),
                Velocity = new Vector3(-1.5f, 0.5f, 100.125f),
                Yaw = 1.75f,
                CoyoteRemaining = 0.083f,
                JumpBufferRemaining = 0.125f,
                SkidRemaining = 0.375f,
                Grounded = true,
            },
            AimStance: AimStance.Raised,
            AimSteadyElapsedSec: 0.42f);

        byte[] packet = NetCodec.PackSnapshot(snap);
        Assert.Equal(58, packet.Length);
        Assert.Equal(NetCodec.SnapshotBytes, packet.Length);

        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(packet);

        Assert.NotNull(got);
        NetCodec.Snapshot g = got!.Value;
        Assert.Equal(snap.Tick, g.Tick);
        Assert.Equal(snap.Epoch, g.Epoch);
        Assert.Equal(snap.LastProcessedSeq, g.LastProcessedSeq);
        Assert.Equal(snap.State.Position, g.State.Position);
        Assert.Equal(snap.State.Velocity, g.State.Velocity);
        Assert.Equal(snap.State.Yaw, g.State.Yaw);
        Assert.Equal(snap.State.CoyoteRemaining, g.State.CoyoteRemaining);
        Assert.Equal(snap.State.JumpBufferRemaining, g.State.JumpBufferRemaining);
        Assert.Equal(snap.State.Grounded, g.State.Grounded);
        Assert.Equal(snap.State.SkidRemaining, g.State.SkidRemaining);
        Assert.Equal(snap.AimStance, g.AimStance);
        Assert.Equal(snap.AimSteadyElapsedSec, g.AimSteadyElapsedSec);
    }

    [Fact]
    public void Snapshot_GroundedFalse_RoundTrips()
    {
        var snap = new NetCodec.Snapshot(1, 0, 2, new MoveState
        {
            Position = Vector3.Zero,
            Velocity = Vector3.Zero,
            Yaw = 0,
            CoyoteRemaining = 0,
            JumpBufferRemaining = 0,
            Grounded = false,
        });

        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));

        Assert.NotNull(got);
        Assert.False(got!.Value.State.Grounded);
    }

    [Theory]
    [InlineData(AimStance.Lowered)]
    [InlineData(AimStance.Raising)]
    [InlineData(AimStance.Raised)]
    [InlineData(AimStance.Lowering)]
    public void Snapshot_EveryAimStanceOrdinal_RoundTrips(AimStance stance)
    {
        var snap = new NetCodec.Snapshot(5, 1, 5, new MoveState { Grounded = true }, stance, 0.5f);
        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));
        Assert.NotNull(got);
        Assert.Equal(stance, got!.Value.AimStance);
    }

    [Fact]
    public void Snapshot_ConstructedWithoutAimArgs_DefaultsToLoweredAndZero()
    {
        // The additive-default contract (NetCodec.Snapshot's own doc comment): a pre-existing
        // 4-arg construction (movement-only) must keep compiling AND keep meaning "Lowered, 0".
        var snap = new NetCodec.Snapshot(1, 0, 1, new MoveState { Grounded = true });
        Assert.Equal(AimStance.Lowered, snap.AimStance);
        Assert.Equal(0f, snap.AimSteadyElapsedSec);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    [InlineData(uint.MaxValue - 1u)]
    public void Snapshot_TickBoundaries_RoundTrip(uint tick)
    {
        var snap = new NetCodec.Snapshot(tick, 7, tick, new MoveState { Grounded = false });
        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));
        Assert.NotNull(got);
        Assert.Equal(tick, got!.Value.Tick);
        Assert.Equal(tick, got.Value.LastProcessedSeq);
    }

    private static void AssertEntryEqual(NetCodec.InputEntry expected, NetCodec.InputEntry actual)
    {
        Assert.Equal(expected.Intent.Seq, actual.Intent.Seq);
        Assert.Equal(expected.Intent.MoveDir, actual.Intent.MoveDir);
        Assert.Equal(expected.Intent.Jump, actual.Intent.Jump);
        Assert.Equal(expected.Intent.Interact, actual.Intent.Interact);
        Assert.Equal(expected.Intent.Throw, actual.Intent.Throw);
        Assert.Equal(expected.Intent.Sprint, actual.Intent.Sprint);
        Assert.Equal(expected.Intent.JumpHeld, actual.Intent.JumpHeld);
        Assert.Equal(expected.Intent.AimRaise, actual.Intent.AimRaise);
        Assert.Equal(expected.Intent.AimYaw, actual.Intent.AimYaw);
        Assert.Equal(expected.Intent.AimPitch, actual.Intent.AimPitch);
        Assert.Equal(expected.SpeedFactor, actual.SpeedFactor);
    }
}
