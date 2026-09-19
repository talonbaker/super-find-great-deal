using Godot;
using MpFoundation.Net;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// The two wire surfaces water state crosses, and the spec §12 requirement that "swim state and
/// chill survive a late-join dump round-trip":
///
/// <list type="bullet">
/// <item><see cref="WaterDump"/> — the late-join packet.</item>
/// <item>The three bits W2 added to <c>NetCodec</c>'s snapshot flags byte — the per-tick path.</item>
/// </list>
///
/// Both are fuzzed as well as round-tripped, because both are parsed from bytes a hostile client
/// controls and neither may ever throw inside a Godot C# network callback.
/// </summary>
public class WaterDumpTests
{
    // --- The late-join dump ---------------------------------------------------------------------

    [Fact]
    public void Dump_RoundTripsEveryFieldForEveryPeer()
    {
        var sent = new List<WaterPeerSnapshot>
        {
            new(1, WaterState.Dry, 0f, false, SputterPhase.None),
            new(487_112, WaterState.Wading, 0.25f, true, SputterPhase.None),
            new(-9, WaterState.Swimming, 0.99f, false, SputterPhase.GoingUnder),
            new(3, WaterState.Dry, 1f, true, SputterPhase.Recovering),
        };
        List<WaterPeerSnapshot> got = WaterDump.Unpack(WaterDump.Pack(sent));
        Assert.Equal(sent.Count, got.Count);
        for (int i = 0; i < sent.Count; i++)
        {
            Assert.Equal(sent[i].PeerId, got[i].PeerId);
            Assert.Equal(sent[i].State, got[i].State);
            Assert.Equal(sent[i].Chill, got[i].Chill, precision: 6);
            Assert.Equal(sent[i].Soaked, got[i].Soaked);
            Assert.Equal(sent[i].Phase, got[i].Phase);
        }
    }

    [Fact]
    public void Dump_MidSwimStateSurvivesTheRoundTrip()
    {
        // The literal spec §12 line. A late joiner must receive the swimmer as swimming, with the
        // chill they actually have — not as a dry camper standing bolt upright in deep water.
        var swimmer = new WaterPeerSnapshot(42, WaterState.Swimming, 0.6137f, false, SputterPhase.None);
        WaterPeerSnapshot got = Assert.Single(WaterDump.Unpack(WaterDump.Pack(new[] { swimmer })));
        Assert.Equal(WaterState.Swimming, got.State);
        Assert.Equal(0.6137f, got.Chill, precision: 6);
    }

    [Fact]
    public void Dump_EmptySessionRoundTripsToEmpty()
    {
        Assert.Empty(WaterDump.Unpack(WaterDump.Pack(Array.Empty<WaterPeerSnapshot>())));
    }

    [Fact]
    public void Dump_PositiveControl_TheComparisonCanFail()
    {
        // Every "round-trips correctly" assertion above is vacuous unless a WRONG packet would be
        // caught. Flip one byte of the state field and confirm the comparison notices.
        byte[] packet = WaterDump.Pack(new[]
        {
            new WaterPeerSnapshot(7, WaterState.Swimming, 0.5f, false, SputterPhase.None),
        });
        packet[5] = (byte)WaterState.Dry; // peerId(4) sits at 1..4, state at 5
        Assert.Equal(WaterState.Dry, Assert.Single(WaterDump.Unpack(packet)).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 3 })]                 // claims 3 entries, carries none
    [InlineData(new byte[] { 0, 0, 0 })]           // claims 0, carries trailing bytes
    [InlineData(new byte[] { 255, 1, 2, 3 })]      // over MaxEntries
    public void Dump_MalformedPacket_YieldsEmptyRatherThanThrowing(byte[]? packet)
    {
        Assert.Empty(WaterDump.Unpack(packet));
    }

    [Fact]
    public void Dump_RandomBytes_NeverThrowAndNeverProduceAnIllegalState()
    {
        var rng = new Random(20260808);
        for (int i = 0; i < 20_000; i++)
        {
            var buf = new byte[rng.Next(0, 40)];
            rng.NextBytes(buf);
            foreach (WaterPeerSnapshot snap in WaterDump.Unpack(buf))
            {
                Assert.True(snap.State <= WaterState.Swimming);
                Assert.True(snap.Phase <= SputterPhase.Recovering);
                Assert.True(float.IsFinite(snap.Chill) && snap.Chill is >= 0f and <= 1f);
            }
        }
    }

    [Fact]
    public void Dump_OversizedInput_IsTruncatedToTheBoundRatherThanRejected()
    {
        var many = new List<WaterPeerSnapshot>();
        for (int i = 0; i < WaterDump.MaxEntries + 20; i++)
            many.Add(new WaterPeerSnapshot(i, WaterState.Dry, 0f, false, SputterPhase.None));
        Assert.Equal(WaterDump.MaxEntries, WaterDump.Unpack(WaterDump.Pack(many)).Count);
    }

    [Fact]
    public void Snapshot_Sanitize_FoldsPoisonedValuesToLegalOnes()
    {
        WaterPeerSnapshot clean = new WaterPeerSnapshot(
            1, (WaterState)200, float.NaN, true, (SputterPhase)200).Sanitized();
        Assert.Equal(WaterState.Dry, clean.State);
        Assert.Equal(SputterPhase.None, clean.Phase);
        Assert.Equal(0f, clean.Chill, precision: 5);
        Assert.True(clean.Soaked); // a legal value is left alone
    }

    // --- The per-tick path: NetCodec's snapshot flags byte --------------------------------------------

    [Theory]
    [InlineData(WaterState.Dry, false, false)]
    [InlineData(WaterState.Wading, false, false)]
    [InlineData(WaterState.Swimming, false, false)]
    [InlineData(WaterState.Swimming, true, true)]
    [InlineData(WaterState.Dry, true, false)]
    [InlineData(WaterState.Dry, false, true)]
    public void SnapshotFlags_RoundTripWaterSoakedAndControlLocked(
        WaterState water, bool soaked, bool locked)
    {
        var snap = new NetCodec.Snapshot(11, 2, 10, new MoveState
        {
            Position = new Vector3(-70f, -1.83f, 4f),
            Velocity = new Vector3(1f, 0f, 0f),
            Grounded = false,
            Water = water,
            Soaked = soaked,
            ControlLocked = locked,
        });
        NetCodec.Snapshot? maybe = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));
        Assert.NotNull(maybe);
        NetCodec.Snapshot got = maybe!.Value;
        Assert.Equal(water, got.State.Water);
        Assert.Equal(soaked, got.State.Soaked);
        Assert.Equal(locked, got.State.ControlLocked);
    }

    [Fact]
    public void SnapshotFlags_DoNotDisturbGrounded_TheBitTheySharedAByteWith()
    {
        // The water bits were carved out of the same byte Grounded already lived in. If the mask
        // or the shift were wrong, the most likely symptom is a swimming camper reading as
        // grounded (or vice versa) — subtle, and it would corrupt gravity rather than water.
        foreach (bool grounded in new[] { true, false })
        {
            var snap = new NetCodec.Snapshot(1, 0, 1, new MoveState
            {
                Grounded = grounded,
                Water = WaterState.Swimming,
                Soaked = true,
                ControlLocked = true,
            });
            NetCodec.Snapshot? maybe = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));
            Assert.NotNull(maybe);
            Assert.Equal(grounded, maybe!.Value.State.Grounded);
            Assert.Equal(WaterState.Swimming, maybe.Value.State.Water);
        }
    }

    [Fact]
    public void SnapshotFlags_TheIllegalTwoBitValueThreeFoldsToDry()
    {
        // Only a doctored packet can produce it (nothing packs 3), so this is exercised by
        // writing the byte directly.
        byte[] packet = NetCodec.PackSnapshot(new NetCodec.Snapshot(1, 0, 1, new MoveState()));
        packet[5] = 0b0000_0110; // both water bits set = 3
        NetCodec.Snapshot? maybe = NetCodec.UnpackSnapshot(packet);
        Assert.NotNull(maybe);
        Assert.Equal(WaterState.Dry, maybe!.Value.State.Water);
    }

    [Fact]
    public void SnapshotFlags_CostNoPacketLength()
    {
        // The whole point of spending spare bits rather than appending a field: the water contract
        // did not grow the packet. Asserted as "every flag combination packs to the same length as
        // the empty state" rather than against a literal — SKID-1 later appended a float for a
        // reason unrelated to these bits (NetCodec.SnapshotBytes, protocol v11), and a literal here
        // would have made that an unrelated red instead of the deliberate, documented change it is.
        int empty = NetCodec.PackSnapshot(new NetCodec.Snapshot(1, 0, 1, new MoveState())).Length;
        Assert.Equal(NetCodec.SnapshotBytes, empty);
        foreach (WaterState w in new[] { WaterState.Dry, WaterState.Wading, WaterState.Swimming })
            Assert.Equal(empty, NetCodec.PackSnapshot(new NetCodec.Snapshot(1, 0, 1,
                new MoveState { Water = w, Soaked = true, ControlLocked = true })).Length);
    }
}
