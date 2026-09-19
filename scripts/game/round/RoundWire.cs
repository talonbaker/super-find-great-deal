using System;
using System.Collections.Immutable;
using System.Linq;

namespace MpFoundation.Game.Round;

/// <summary>One rider's live line on the wire: carried coins and shatters this round, both
/// clamped to a byte. No witnessed count here — that only exists on a tally line (D3: witnessed
/// shatters are card content, not live HUD content).</summary>
public readonly record struct RoundWireRider(int RiderId, byte CarriedCoins, byte Shatters);

/// <summary>One rider's line on the frozen tally, wire-clamped.</summary>
public readonly record struct RoundWireTallyLine(int RiderId, byte Coins, byte Shatters, byte WitnessedShatters);

/// <summary>
/// <b>The replicated round message</b> (SESSION-2 item 6; SN-2's "absolute ints on one channel"
/// shape, reserved for <c>RoundChannel = 21</c> in the design doc only — <c>NetProfile.cs</c> is
/// not touched in phase A).
///
/// <para><b>Absolute values only.</b> Every field is what IS, not what changed, which is the
/// whole late-join property: <see cref="Fold"/> ignores whatever view a peer already had and
/// rebuilds one from this message alone (MECHANICS §4 — applying the same message twice, or
/// applying it with no prior state at all, produces the identical result).</para>
///
/// <para><b>Byte arithmetic, EXCEPT the identity fields</b> (corrected in phase B, SESSION-2b,
/// 2026-09-15, by a real engine run — not reasoned about). <see cref="RoundWireRider.CarriedCoins"/>,
/// <see cref="RoundWireRider.Shatters"/> and the tally line's three counters are <c>u8</c> and
/// CLAMP at 255 rather than wrap — a coin count topping 255 reads as 255 on the wire (and stays
/// correct again the instant it is countable), which is the honest failure for a display value;
/// a wrapped byte reading back as a small number while the real count is 260 is the dishonest one.
/// <see cref="Round"/> and <see cref="RemainingTenths"/> are <c>u16</c> for the same reason at a
/// wider range.
///
/// <para><b>Rider identity is NOT a byte, and phase A's own design was wrong about that.</b>
/// Phase A clamped a rider's id (a Godot multiplayer peer id, an arbitrary 32-bit value) to a byte
/// on the same "honest clamp" reasoning as the score fields. On a live run with real ENet peer ids
/// this is not an edge case: two riders whose real ids both exceed 255 clamp to the identical
/// byte 255, `RoundWireRider`/`RoundWireTallyLine` carry two rows claiming the same identity, and
/// <c>Fold</c>'s `ToImmutableDictionary` throws on the duplicate key — measured on
/// `Run-RoundLoopTest.ps1`'s very first run, reliably once a fourth rider joined. A clamped
/// identity is not a legible degradation the way a clamped score is (a coin count of 255 is still
/// "a lot of coins"; two different players both reading as rider 255 is silent data loss with no
/// honest reading at all), so identity fields are carried as the plain <c>int</c> they already are
/// in <see cref="RoundLoopState"/> — four bytes on the wire instead of one, and correct instead of
/// fast.</para></summary>
public readonly record struct RoundWire(
    byte Phase,
    ushort Round,
    ushort RemainingTenths,
    ImmutableArray<RoundWireRider> Riders,
    ImmutableArray<int> WinnerRiderIds,
    ImmutableArray<RoundWireTallyLine> TallyLines)
{
    /// <summary>Clamp, never wrap. A coin or shatter count above 255 reads back as 255, not as
    /// itself modulo 256. NOT used for a rider identity — see the class doc's correction.</summary>
    public static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>Clamp, never wrap, at the wider range.</summary>
    public static ushort ClampUShort(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);

    /// <summary>
    /// <b>Encode.</b> <paramref name="riderOrder"/> is the roster this message describes, in the
    /// order the caller wants the arrays walked — the wire carries each rider's own id alongside
    /// their line (rather than a bare positional array with the id implied by a separate roster
    /// channel), specifically so <see cref="Fold"/> needs nothing else to reconstruct a complete
    /// view. A rider absent from <paramref name="riderOrder"/> is simply not on this message.
    /// </summary>
    public static RoundWire Encode(RoundLoopState s, System.Collections.Generic.IEnumerable<int> riderOrder)
    {
        ImmutableArray<RoundWireRider> riders = riderOrder
            .Select(id => new RoundWireRider(
                id,
                ClampByte(s.CarriedCoinsPerRider.TryGetValue(id, out int c) ? c : 0),
                ClampByte(s.ShattersPerRider.TryGetValue(id, out int sh) ? sh : 0)))
            .ToImmutableArray();

        ImmutableArray<int> winners = ImmutableArray<int>.Empty;
        ImmutableArray<RoundWireTallyLine> tallyLines = ImmutableArray<RoundWireTallyLine>.Empty;
        if (s.LastTally is { } tally)
        {
            winners = tally.WinnerRiderIds.ToImmutableArray();
            tallyLines = tally.Lines
                .Select(l => new RoundWireTallyLine(
                    l.RiderId, ClampByte(l.Coins), ClampByte(l.Shatters), ClampByte(l.WitnessedShatters)))
                .ToImmutableArray();
        }

        return new RoundWire(
            Phase: (byte)s.Phase,
            Round: ClampUShort(s.RoundIndex),
            RemainingTenths: ClampUShort((int)MathF.Round(Math.Max(s.RemainingSec, 0f) * 10f)),
            Riders: riders,
            WinnerRiderIds: winners,
            TallyLines: tallyLines);
    }

    /// <summary>
    /// <b>Fold.</b> Rebuilds a complete local view from this message alone. <paramref
    /// name="previous"/> is accepted and then IGNORED for every field — deliberately: a real fold
    /// combines an accumulator with a new value, and the point being proven here is that this one
    /// does not need to. <c>Fold(null, wire)</c> and <c>Fold(someOldView, wire)</c> are
    /// byte-identical for the same <paramref name="wire"/>, which is what "a late joiner is
    /// complete from one message" means as a testable property.
    /// </summary>
    public static RoundWireView Fold(RoundWireView? previous, in RoundWire wire)
    {
        _ = previous; // deliberately unused — see the summary above.

        ImmutableDictionary<int, int> coins = wire.Riders
            .ToImmutableDictionary(r => r.RiderId, r => (int)r.CarriedCoins);
        ImmutableDictionary<int, int> shatters = wire.Riders
            .ToImmutableDictionary(r => r.RiderId, r => (int)r.Shatters);

        RoundTallyResult? tally = wire.TallyLines.IsDefaultOrEmpty && wire.WinnerRiderIds.IsDefaultOrEmpty
            ? null
            : new RoundTallyResult(
                wire.WinnerRiderIds,
                wire.TallyLines
                    .Select(l => new RoundTallyLine(l.RiderId, l.Coins, l.Shatters, l.WitnessedShatters))
                    .ToImmutableArray());

        return new RoundWireView(
            (RoundPhase)wire.Phase,
            wire.Round,
            wire.RemainingTenths / 10f,
            coins,
            shatters,
            tally);
    }
}

/// <summary>A folded, display-ready view of one <see cref="RoundWire"/> message — what a client,
/// including a peer that just joined, has to show a HUD and the tally card.</summary>
public readonly record struct RoundWireView(
    RoundPhase Phase,
    int Round,
    float RemainingSec,
    ImmutableDictionary<int, int> CarriedCoinsPerRider,
    ImmutableDictionary<int, int> ShattersPerRider,
    RoundTallyResult? LastTally);
