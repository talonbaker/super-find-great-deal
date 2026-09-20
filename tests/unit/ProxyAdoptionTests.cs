using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>What a remote proxy adopts out of a snapshot</b> — MOVE-5e, gap 1.
///
/// <para>A proxy never runs <c>AvatarMotor.Step</c>, so its <c>MoveState</c> is whatever
/// <c>SandboxAvatar.ApplySnapshot</c> puts there and nothing else — and that state is what the
/// pose layer reads on every role. Before MOVE-5e the adoption was a hand-written list of the
/// fields somebody had remembered, and it had been wrong three times: <c>SkidRemaining</c> arrived
/// a wave late, and <c>Incapacity</c>, <c>ImpulseRagdoll</c> and all five MOVE-5 verb fields were
/// never added at all.
///
/// <para><b>The point of this class is the first test, not the specific fields.</b>
/// <see cref="EveryFieldIsAdoptedExceptTheFourRenderInterpolatedOnes"/> walks <c>MoveState</c> by
/// reflection, so a field added to that struct tomorrow lands here as a failure rather than as a
/// teammate's body that is silently wrong. That is a different kind of test from "does the current
/// list contain Verb": it fails for the NEXT instance of the bug, not this one.</para>
/// </summary>
public class ProxyAdoptionTests
{
    /// <summary>
    /// The four fields <c>AdoptForProxy</c> deliberately leaves alone, because
    /// <c>SnapshotBuffer</c> already delivers them to a proxy resolved at the RENDER tick and
    /// <c>SandboxAvatar.RemoteFrame</c> is their single writer. Adopting the newest values as well
    /// would give one fact two disagreeing carriers on one role.
    ///
    /// <para><b>Changing this set is the decision this class exists to make expensive.</b> Anything
    /// added here is a field a proxy will never learn, which is the shape of every defect MOVE-5e
    /// closed.</para>
    /// </summary>
    private static readonly HashSet<string> RenderInterpolated =
        new() { "Position", "Velocity", "Yaw", "Grounded" };

    /// <summary>A state in which every single field differs from <see cref="Snapshot"/>'s. The
    /// "every single" is enforced by <see cref="TheTwoFixturesDifferInEveryField"/> — without that
    /// guard a field the two happened to share would make the main test pass vacuously about it,
    /// which is precisely how a new field would slip through.</summary>
    private static MoveState Current => new()
    {
        Position = new Vector3(1f, 2f, 3f),
        Velocity = new Vector3(4f, 5f, 6f),
        Yaw = 0.25f,
        CoyoteRemaining = 0.05f,
        JumpBufferRemaining = 0.06f,
        SkidRemaining = 0.07f,
        Grounded = true,
        Water = Sail.Game.Water.WaterState.Dry,
        Soaked = false,
        ControlLocked = false,
        Incapacity = Sail.Game.Failure.IncapacityState.Active,
        ImpulseRagdoll = false,
        Verb = MoveVerb.Normal,
        VerbClockTicks = 3,
        ChainDepth = 0,
        ChainTimerTicks = 9,
        AirJumpsUsed = 0,
    };

    private static MoveState Snapshot => new()
    {
        Position = new Vector3(-7f, -8f, -9f),
        Velocity = new Vector3(-10f, -11f, -12f),
        Yaw = -1.5f,
        CoyoteRemaining = 0.11f,
        JumpBufferRemaining = 0.12f,
        SkidRemaining = 0.13f,
        Grounded = false,
        Water = Sail.Game.Water.WaterState.Swimming,
        Soaked = true,
        ControlLocked = true,
        Incapacity = Sail.Game.Failure.IncapacityState.Frozen,
        ImpulseRagdoll = true,
        Verb = MoveVerb.DuckWalk,
        VerbClockTicks = 42,
        ChainDepth = 3,
        ChainTimerTicks = 17,
        AirJumpsUsed = 2,
    };

    private static FieldInfo[] Fields =>
        typeof(MoveState).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>POSITIVE CONTROL for every other test in this class. If the two fixtures agree
    /// about any field, the adoption test below proves nothing about that field — it would pass
    /// whether the field was adopted, excluded, or dropped on the floor. This is also the test that
    /// fires when somebody adds a field to <c>MoveState</c> and does not come here.</summary>
    [Fact]
    public void TheTwoFixturesDifferInEveryField()
    {
        MoveState a = Current;
        MoveState b = Snapshot;
        foreach (FieldInfo f in Fields)
        {
            Assert.False(Equals(f.GetValue(a), f.GetValue(b)),
                $"MoveState.{f.Name} is identical in both fixtures, so every adoption assertion "
                + "about it is vacuous. If you have just added a field to MoveState, give it "
                + "distinct values in ProxyAdoptionTests.Current and .Snapshot — and while you are "
                + "here, decide whether a remote proxy needs it (it almost certainly does).");
        }
    }

    /// <summary>
    /// <b>The audit, mechanised.</b> Every field is adopted from the authoritative snapshot except
    /// the four <see cref="RenderInterpolated"/> ones, which are kept from the proxy's current
    /// state. Reflection rather than seventeen hand-written assertions, so the NEXT field is
    /// covered too.
    /// </summary>
    [Fact]
    public void EveryFieldIsAdoptedExceptTheFourRenderInterpolatedOnes()
    {
        MoveState current = Current;
        MoveState snapshot = Snapshot;
        MoveState adopted = MoveState.AdoptForProxy(current, snapshot);

        foreach (FieldInfo f in Fields)
        {
            bool excluded = RenderInterpolated.Contains(f.Name);
            object? expected = f.GetValue(excluded ? current : snapshot);
            Assert.True(Equals(expected, f.GetValue(adopted)),
                excluded
                    ? $"MoveState.{f.Name} is render-interpolated and must NOT be adopted from the "
                      + "newest snapshot — SnapshotBuffer resolves it at the render tick and "
                      + "RemoteFrame is its single writer."
                    : $"MoveState.{f.Name} never reaches a remote proxy. A proxy runs no motor, so "
                      + "the pose, the skin and the nameplate read exactly this state and nothing "
                      + "else: an un-adopted field is a teammate's body that is permanently wrong "
                      + "in that respect, on every peer but their own.");
        }
    }

    /// <summary>The exclusion set is small and named, and growing it is the one way to reintroduce
    /// this bug class. Pinned so that doing so is a deliberate edit to a test that says why.</summary>
    [Fact]
    public void TheExclusionSetIsExactlyTheFourRenderInterpolatedFields()
    {
        Assert.Equal(
            new[] { "Grounded", "Position", "Velocity", "Yaw" },
            RenderInterpolated.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The five MOVE-5 verb fields specifically — the ones MOVE-5c's animation tells pose
    /// from, and the reason the tells would otherwise be correct on your own body and dead on the
    /// only body they exist for.</summary>
    [Fact]
    public void TheFiveVerbFieldsReachTheProxy()
    {
        MoveState adopted = MoveState.AdoptForProxy(Current, Snapshot);
        Assert.Equal(MoveVerb.DuckWalk, adopted.Verb);
        Assert.Equal(42, adopted.VerbClockTicks);
        Assert.Equal(3, adopted.ChainDepth);
        Assert.Equal(17, adopted.ChainTimerTicks);
        Assert.Equal(2, adopted.AirJumpsUsed);
    }

    /// <summary>Incapacity and the impulse ragdoll — NOT named in the MOVE-5e packet, found by the
    /// field-by-field audit it asked for, and the older and more visible of the two defects.
    /// <c>SandboxAvatar.SyncIncapacitySkin</c> runs on every role every frame and re-derives the
    /// failure skin from exactly this field; <c>UpdateNameplate</c> reads it to drop a knocked-out
    /// camper's plate onto the collapsed silhouette. Un-adopted, a proxy held both at Active
    /// forever: a teammate who was knocked out kept a standing skin and a nameplate floating a
    /// metre above them — which is this repo's own named recurring failure, quoted in the comment
    /// directly above the code that computes that height.</summary>
    [Fact]
    public void IncapacityAndImpulseRagdollReachTheProxy()
    {
        MoveState adopted = MoveState.AdoptForProxy(Current, Snapshot);
        Assert.Equal(Sail.Game.Failure.IncapacityState.Frozen, adopted.Incapacity);
        Assert.True(adopted.ImpulseRagdoll);
    }

    /// <summary>Idempotent: adopting the same snapshot twice is adopting it once. A proxy runs this
    /// on every snapshot at 30 Hz, and MECHANICS §4 wants that stated rather than assumed.</summary>
    [Fact]
    public void AdoptionIsIdempotent()
    {
        MoveState once = MoveState.AdoptForProxy(Current, Snapshot);
        MoveState twice = MoveState.AdoptForProxy(once, Snapshot);
        Assert.Equal(once, twice);
    }
}
