using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

public class StateChecksumTests
{
    private static MoveState State(Vector3 pos, Vector3 vel, float yaw, bool grounded = true) =>
        new() { Position = pos, Velocity = vel, Yaw = yaw, Grounded = grounded };

    [Fact]
    public void IdenticalStates_HashEqual()
    {
        var a = State(new Vector3(1, 2, 3), new Vector3(0.5f, 0, -0.5f), 1.2f);
        var b = State(new Vector3(1, 2, 3), new Vector3(0.5f, 0, -0.5f), 1.2f);
        Assert.Equal(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    [Fact]
    public void SubQuantumDifference_HashesEqual()
    {
        // Below the 1 mm / ~0.35 deg quantum, float noise must NOT read as a desync.
        var a = State(new Vector3(1f, 2f, 3f), Vector3.Zero, 1.0f);
        var b = State(new Vector3(1.0004f, 2f, 3f), new Vector3(0.0004f, 0, 0), 1.003f);
        Assert.Equal(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    [Fact]
    public void MeaningfulPositionDifference_HashesDifferently()
    {
        var a = State(new Vector3(1f, 2f, 3f), Vector3.Zero, 0f);
        var b = State(new Vector3(1.5f, 2f, 3f), Vector3.Zero, 0f); // 0.5 m apart
        Assert.NotEqual(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    [Fact]
    public void GroundedFlipChangesHash()
    {
        var a = State(Vector3.Zero, Vector3.Zero, 0f, grounded: true);
        var b = State(Vector3.Zero, Vector3.Zero, 0f, grounded: false);
        Assert.NotEqual(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    [Fact]
    public void NonFiniteFields_DoNotThrow()
    {
        var a = State(new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), float.NaN);
        _ = StateChecksum.Of(a); // must be total — a poisoned state hashes to something, never throws
    }

    [Fact]
    public void SignMatters()
    {
        var a = State(new Vector3(1f, 0, 0), Vector3.Zero, 0f);
        var b = State(new Vector3(-1f, 0, 0), Vector3.Zero, 0f);
        Assert.NotEqual(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    // --- THE DISCRETE FACTS (MOVE-5e) ------------------------------------------------------------
    // The class calls itself a checksum "of an authoritative MoveState" and hashed exactly one of
    // the struct's eleven discrete facts (Grounded, above). Two states differing ONLY in whether
    // the body was frozen, locked, swimming, sliding or ducked hashed identically — the same blind
    // spot PredictionMatches had, in the tool that exists to make a desync legible after the fact.
    //
    // A [Theory] rather than eleven [Fact]s so the failure names the field, and so adding the
    // twelfth is one line here.

    /// <summary>Every discrete fact changes the hash. The mutator is applied to an otherwise
    /// identical state, so nothing but the named field can be responsible.</summary>
    [Theory]
    [MemberData(nameof(DiscreteFacts))]
    public void EachDiscreteFactChangesTheHash(string field)
    {
        MoveState a = State(new Vector3(3f, 1f, -2f), new Vector3(1f, 0f, 0f), 0.4f);
        MoveState b = a;
        Mutate(field, ref b);
        Assert.NotEqual(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    /// <summary>POSITIVE CONTROL for the theory above: the unmutated fixture hashes to itself. A
    /// NotEqual assertion is worthless if the two sides were never equal to begin with.</summary>
    [Fact]
    public void TheDiscreteFactFixtureHashesToItself()
    {
        MoveState a = State(new Vector3(3f, 1f, -2f), new Vector3(1f, 0f, 0f), 0.4f);
        MoveState b = a;
        Assert.Equal(StateChecksum.Of(a), StateChecksum.Of(b));
    }

    /// <summary>The skid enters the hash as the BOOLEAN, for the reason
    /// <c>SandboxAvatar.PredictionMatches</c> compares it that way: it is a float decremented by
    /// <c>dt</c>, so hashing the raw value would make the checksum differ on a sub-tick clock offset
    /// between two peers that agree about everything that matters. Two mid-skid states with
    /// different remaining times hash the same; skidding versus not does not.</summary>
    [Fact]
    public void TheSkidIsHashedAsABooleanNotAsItsTimer()
    {
        MoveState a = State(Vector3.Zero, Vector3.Zero, 0f);
        MoveState b = a;
        a.SkidRemaining = 0.20f;
        b.SkidRemaining = 0.19f;
        Assert.Equal(StateChecksum.Of(a), StateChecksum.Of(b));

        MoveState notSkidding = State(Vector3.Zero, Vector3.Zero, 0f);
        Assert.NotEqual(StateChecksum.Of(a), StateChecksum.Of(notSkidding));
    }

    /// <summary>The bit field packs eleven facts into one word, so the assertion that actually
    /// matters is that no two of them alias into the same bits. Flipping each in turn from the same
    /// base must produce eleven DISTINCT hashes — an aliasing bug would show up as a collision here
    /// and nowhere else.</summary>
    [Fact]
    public void NoTwoDiscreteFactsAliasIntoTheSameBits()
    {
        MoveState baseState = State(new Vector3(3f, 1f, -2f), new Vector3(1f, 0f, 0f), 0.4f);
        var seen = new Dictionary<uint, string>();
        foreach (object[] row in DiscreteFacts)
        {
            var field = (string)row[0];
            MoveState mutated = baseState;
            Mutate(field, ref mutated);
            uint hash = StateChecksum.Of(mutated);
            Assert.False(seen.ContainsKey(hash),
                $"MoveState.{field} and MoveState.{seen.GetValueOrDefault(hash)} produce the same "
                + "checksum, so the two are sharing bits in the packed word and a divergence in "
                + "one is indistinguishable from a divergence in the other");
            seen[hash] = field;
        }
        Assert.Equal(DiscreteFacts.Count, seen.Count);
    }

    /// <summary>The eleven discrete facts on <c>MoveState</c>, by name. Adding a field to that
    /// struct without adding it here leaves it unhashed and silent; adding it here without hashing
    /// it turns <see cref="EachDiscreteFactChangesTheHash"/> red, which is the correct direction for
    /// this list to fail in.</summary>
    public static readonly List<object[]> DiscreteFacts = new()
    {
        new object[] { "Grounded" },
        new object[] { "Soaked" },
        new object[] { "ControlLocked" },
        new object[] { "ImpulseRagdoll" },
        new object[] { "SkidRemaining" },
        new object[] { "Water" },
        new object[] { "Incapacity" },
        new object[] { "Verb" },
        new object[] { "ChainDepth" },
        new object[] { "AirJumpsUsed" },
        new object[] { "VerbClockTicks" },
        new object[] { "ChainTimerTicks" },
    };

    /// <summary>One mutation per discrete fact, away from the fixture's value. Written as a switch
    /// rather than as delegates in the MemberData because xUnit serialises theory data and a
    /// delegate over a by-ref struct will not survive that trip.</summary>
    private static void Mutate(string field, ref MoveState s)
    {
        switch (field)
        {
            case "Grounded": s.Grounded = !s.Grounded; break;
            case "Soaked": s.Soaked = !s.Soaked; break;
            case "ControlLocked": s.ControlLocked = !s.ControlLocked; break;
            case "ImpulseRagdoll": s.ImpulseRagdoll = !s.ImpulseRagdoll; break;
            case "SkidRemaining": s.SkidRemaining = 0.2f; break;
            case "Water": s.Water = Sail.Game.Water.WaterState.Swimming; break;
            case "Incapacity": s.Incapacity = Sail.Game.Failure.IncapacityState.Frozen; break;
            case "Verb": s.Verb = MoveVerb.DuckWalk; break;
            case "ChainDepth": s.ChainDepth = 3; break;
            case "AirJumpsUsed": s.AirJumpsUsed = 2; break;
            case "VerbClockTicks": s.VerbClockTicks = 42; break;
            case "ChainTimerTicks": s.ChainTimerTicks = 17; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field,
                "unknown discrete fact — add a mutation for it, and while you are here check that "
                + "StateChecksum.Of hashes it at all");
        }
    }
}
