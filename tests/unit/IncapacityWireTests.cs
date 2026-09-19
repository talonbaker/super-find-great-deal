using Godot;
using MpFoundation.Net;
using Sail.Game.Failure;
using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// The failure states on the wire (protocol v10) and through the motor.
///
/// <para><b>What this file exists to prevent.</b> Two things, and both have already happened in
/// this repo in one form or another. First: a state that influences movement living anywhere but
/// <c>MoveState</c>, so that a reconciliation replay re-grants the player control for every
/// replayed tick — the controllable-ragdoll defect arriving through the reconciliation door.
/// Second: a snapshot flags-byte field that packs and unpacks asymmetrically, or that collides
/// with the water bits packed beside it, which would be silently wrong on the wire and visible
/// only as "sometimes a teammate looks fine when they are frozen".</para>
/// </summary>
public class IncapacityWireTests
{
    private const float Dt = 1f / 60f;

    private static NetCodec.Snapshot RoundTrip(MoveState state)
    {
        byte[] packet = NetCodec.PackSnapshot(new NetCodec.Snapshot(11u, 3, 42u, state));
        NetCodec.Snapshot? back = NetCodec.UnpackSnapshot(packet);
        Assert.NotNull(back);
        return back!.Value;
    }

    [Theory]
    [InlineData(IncapacityState.Active)]
    [InlineData(IncapacityState.KnockedOut)]
    [InlineData(IncapacityState.Frozen)]
    public void EveryState_SurvivesTheRoundTrip(IncapacityState state)
    {
        NetCodec.Snapshot back = RoundTrip(new MoveState { Incapacity = state });
        Assert.Equal(state, back.State.Incapacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheImpulseBit_SurvivesTheRoundTrip(bool impulse)
    {
        NetCodec.Snapshot back = RoundTrip(new MoveState { ImpulseRagdoll = impulse });
        Assert.Equal(impulse, back.State.ImpulseRagdoll);
    }

    /// <summary>The whole flags byte at once. The two incapacity bits sit directly above the
    /// water field and directly below the impulse bit, so a shift or mask error would show up as
    /// one field corrupting another rather than as a field failing on its own.</summary>
    [Fact]
    public void TheFlagsByte_CarriesEveryFieldWithoutCollision()
    {
        var state = new MoveState
        {
            Grounded = true,
            Water = WaterState.Swimming,
            Soaked = true,
            ControlLocked = true,
            Incapacity = IncapacityState.Frozen,
            ImpulseRagdoll = true,
        };
        NetCodec.Snapshot back = RoundTrip(state);
        Assert.True(back.State.Grounded);
        Assert.Equal(WaterState.Swimming, back.State.Water);
        Assert.True(back.State.Soaked);
        Assert.True(back.State.ControlLocked);
        Assert.Equal(IncapacityState.Frozen, back.State.Incapacity);
        Assert.True(back.State.ImpulseRagdoll);
    }

    [Fact]
    public void KnockedOut_DoesNotLeakIntoTheWaterField()
    {
        NetCodec.Snapshot back = RoundTrip(new MoveState
        {
            Incapacity = IncapacityState.KnockedOut,
            Water = WaterState.Dry,
        });
        Assert.Equal(WaterState.Dry, back.State.Water);
        Assert.Equal(IncapacityState.KnockedOut, back.State.Incapacity);
        Assert.False(back.State.Soaked);
        Assert.False(back.State.ControlLocked);
    }

    /// <summary>Phase 1c cost the snapshot NO extra bytes — the two incapacity bits and the ragdoll
    /// bit rode spare bits of the flags byte — which is exactly why <c>NetProfile.ProtocolVersion</c>
    /// HAD to go to 10: a v9 peer would parse a v10 snapshot with no error at all and simply never
    /// see anyone go down.
    ///
    /// <para>The assertion is against <c>NetCodec.SnapshotBytes</c> rather than a literal, since
    /// SKID-1 later moved the layout to 55 for a reason that had nothing to do with these bits. What
    /// this test still pins is the property that mattered: setting them changes no length.</para>
    /// </summary>
    [Fact]
    public void TheIncapacityBitsCostNoPacketLength()
    {
        byte[] plain = NetCodec.PackSnapshot(new NetCodec.Snapshot(1u, 0, 0u, new MoveState()));
        byte[] packet = NetCodec.PackSnapshot(new NetCodec.Snapshot(1u, 0, 0u,
            new MoveState { Incapacity = IncapacityState.Frozen, ImpulseRagdoll = true }));
        Assert.Equal(NetCodec.SnapshotBytes, packet.Length);
        Assert.Equal(plain.Length, packet.Length);
    }

    /// <summary>The reserved fourth value of the two-bit field folds to Active. It exists so a
    /// third state can be added later without a wire change, which means a build with one WILL
    /// eventually send it to a build without — and "boring and legal" is the only safe reading of
    /// a state you do not know. Same discipline as the water field's fold to Dry.</summary>
    [Fact]
    public void TheReservedFourthValue_FoldsToActive()
    {
        byte[] packet = NetCodec.PackSnapshot(new NetCodec.Snapshot(1u, 0, 0u, new MoveState()));
        packet[5] |= 0b0110_0000; // the reserved value 3 in the incapacity field
        NetCodec.Snapshot? back = NetCodec.UnpackSnapshot(packet);
        Assert.NotNull(back);
        Assert.Equal(IncapacityState.Active, back!.Value.State.Incapacity);
    }

    // --- The control-lock decision ---------------------------------------------------------------

    /// <summary>Every state that must stop a player steering, and the one that must not. This is
    /// the decision <c>AvatarMotor.Step</c> makes before it touches the intent, and it is the
    /// single most safety-critical branch in the movement path: everything downstream of it is
    /// "the player is or is not playing the game".</summary>
    [Theory]
    [InlineData(IncapacityState.KnockedOut, false, false, true)]
    [InlineData(IncapacityState.Frozen, false, false, true)]
    [InlineData(IncapacityState.Active, true, false, true)]   // an impulse ragdoll
    [InlineData(IncapacityState.Active, false, true, true)]   // the lake's sputter-out
    [InlineData(IncapacityState.Active, false, false, false)] // ordinary play
    public void ControlLockedBy_CoversEveryReasonAndNoOthers(
        IncapacityState state, bool impulse, bool waterLock, bool expected)
    {
        var prev = new MoveState
        {
            Incapacity = state,
            ImpulseRagdoll = impulse,
            ControlLocked = waterLock,
        };
        Assert.Equal(expected, AvatarMotor.ControlLockedBy(prev));
    }

    /// <summary>The three reasons are independent — none of them can mask another's absence. A
    /// union written as a chain of <c>if</c>s that returned early would pass the theory above and
    /// still get this wrong.</summary>
    [Fact]
    public void EachLockReason_StandsAlone()
    {
        Assert.True(AvatarMotor.ControlLockedBy(new MoveState { ControlLocked = true }));
        Assert.True(AvatarMotor.ControlLockedBy(new MoveState { ImpulseRagdoll = true }));
        Assert.True(AvatarMotor.ControlLockedBy(
            new MoveState { Incapacity = IncapacityState.KnockedOut }));
        Assert.False(AvatarMotor.ControlLockedBy(new MoveState()));
    }

    /// <summary>Spawn state preserves them — a teleport must not un-own a server-set control flag
    /// for a tick. (<c>MoveState.AtSpawn</c> itself is deliberately a clean slate; the preserving
    /// wrapper lives on the avatar, so this pins the base's behaviour so the wrapper's own reason
    /// to exist stays visible.)</summary>
    [Fact]
    public void AtSpawn_IsACleanSlate_WhichIsWhyTheAvatarWrapsIt()
    {
        MoveState fresh = MoveState.AtSpawn(Vector3.Zero);
        Assert.Equal(IncapacityState.Active, fresh.Incapacity);
        Assert.False(fresh.ImpulseRagdoll);
    }
}
