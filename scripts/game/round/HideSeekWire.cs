using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MpFoundation.Game.Round;

/// <summary>One peer's cumulative score, wire-clamped. The score is a byte; the identity is
/// not — see <see cref="HideSeekWire"/>'s class doc for why that asymmetry is deliberate and
/// measured.</summary>
public readonly record struct HideSeekWireScore(int PeerId, byte Score);

/// <summary>
/// The frozen card on the wire. Gains and totals are bytes (display values, clamped); the round
/// index, the match index and every identity are not.
///
/// <para><b>MATCH-1's five fields ride here rather than being recomputed on each client.</b> The
/// match result is a FROZEN fact about a round that has already finished, and the client's own
/// copy of <c>HideSeekTuning</c> is not the server's — deriving "did that end a match" or "who
/// won" locally is the same class of mistake as reading the live <c>Round</c> for a card's own
/// round index, which is what <see cref="HideSeekTally.RoundIndex"/> already exists to prevent.
/// Five fields, four of them a byte or a bool, on a message that is only sent when something
/// changes.</para>
/// </summary>
public readonly record struct HideSeekWireTally(
    int RoundIndex,
    int HiderPeerId,
    byte HiderGained,
    int SeekerPeerId,
    byte SeekerGained,
    bool EndedByDisconnect,
    bool MatchOver = false,
    int MatchIndex = 0,
    int WinnerPeerId = 0,
    byte HiderTotal = 0,
    byte SeekerTotal = 0);

/// <summary>
/// <b>The replicated round message.</b> One absolute value per fact, on
/// <c>NetProfile.RoundChannel</c> (21), broadcast by <c>HideSeekDriver</c> on every change and
/// sent once to every joining peer.
///
/// <para><b>Absolute values only.</b> Every field is what IS, never what changed, and that is the
/// whole late-join property: <see cref="Fold"/> ignores whatever view a peer already had and
/// rebuilds one from this message alone. Applying the same message twice, or applying it with no
/// prior state at all, produces the identical result — <c>Fold(null, wire)</c> and
/// <c>Fold(anyOldView, wire)</c> are equal, and there is a test that says so.</para>
///
/// <para><b>Byte arithmetic for scores, EXCEPT the identity fields — and that exception was paid
/// for.</b> Carried across verbatim from the loop this replaces, whose phase-A design clamped a
/// rider's id to a byte on the same "honest clamp" reasoning as its counters. On a live run with
/// real ENet peer ids that is not an edge case: two players whose ids both exceed 255 clamp to
/// the identical byte, the message then carries two rows claiming the same identity, and the
/// fold's dictionary build throws on the duplicate key — measured, reliably, once a fourth peer
/// joined. A clamped SCORE is a legible degradation (255 is still "a lot"); two different players
/// both reading as peer 255 is silent data loss with no honest reading at all. So every identity
/// on this message is the plain <c>int</c> it already is in <see cref="HideSeekState"/>: four
/// bytes instead of one, and correct instead of fast.</para>
///
/// <para><b>What is deliberately NOT here.</b> The one-shot <see cref="HideSeekState.Refusal"/> IS
/// carried (BTN-1 and the HUD strip both need the sentence) and so is
/// <see cref="HideSeekState.FoundTick"/> — DOOR-1's burst has to be alignable to the SERVER's
/// instant rather than to whenever each peer happened to apply the message, and a driver that
/// raised its <c>Found</c> event with a locally invented tick would be lying about the one number
/// the startle hangs on. <see cref="HideSeekState.Tick"/> and <c>HidingExtended</c> are not here:
/// they are server bookkeeping nothing downstream reads.</para>
///
/// <para><b>This message has no version constant of its own, deliberately.</b> It is versioned by
/// the one number a handshake actually compares, <c>NetProfile.ProtocolVersion</c> — a second
/// version living beside the first is two numbers that have to be bumped together and will not
/// be. MATCH-1 added five fields to <see cref="HideSeekWireTally"/> and deliberately did NOT
/// touch <c>ProtocolVersion</c>, leaving the bump to integration where the wave's total change is
/// visible. <b>That bump is v15 -> v16 and it landed at INT-0B (2026-09-19)</b>, shared with
/// SFX-1's three appended <c>PropKind</c> ordinals: ONE bump carrying both reasons, and
/// <c>NetProfile.ProtocolVersion</c>'s v16 paragraph is where both are written down. Recorded
/// here so the next lane to widen this message knows which number is the real one.</para>
/// </summary>
public readonly record struct HideSeekWire(
    byte Phase,
    ushort Round,
    ushort RemainingTenths,
    int HiderPeerId,
    int SeekerPeerId,
    ImmutableArray<HideSeekWireScore> Scores,
    byte Refusal,
    // TASK-1 (2026-09-19) renamed this from TowersCompleted. SAME SLOT, SAME TYPE, SAME POSITION
    // in Pack/Unpack's tuple -- the byte layout on the wire is untouched, so no ProtocolVersion
    // bump is owed for it and a peer built before the rename would decode this message
    // identically. What changed is the word: the task room sorts objects into bins (Talon,
    // 2026-09-19) and there are no towers in this game.
    ushort SortsCompleted,
    int FoundTick,
    HideSeekWireTally? Tally)
{
    /// <summary>Sentinel for "this message carries no card", used by <see cref="Pack"/> /
    /// <see cref="Unpack"/>. A round index is 1-based, so a negative one cannot collide with a
    /// real card.</summary>
    public const int NoTallyRound = -1;

    /// <summary>Sentinel for "the target has not been found this round". Not 0 — tick 0 is a real
    /// tick.</summary>
    public const int NoFoundTick = -1;

    /// <summary>Clamp, never wrap. A score above 255 reads back as 255, not as itself modulo 256.
    /// NOT used for an identity — see the class doc.</summary>
    public static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>Clamp, never wrap, at the wider range.</summary>
    public static ushort ClampUShort(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);

    /// <summary>
    /// <b>Encode.</b> <paramref name="scoreOrder"/> is the roster this message describes, in the
    /// order the caller wants the array walked. Each row carries its own peer id rather than
    /// implying it from a separate roster channel, specifically so <see cref="Fold"/> needs
    /// nothing else to reconstruct a complete view. A peer absent from
    /// <paramref name="scoreOrder"/> is simply not on this message.
    /// </summary>
    public static HideSeekWire Encode(HideSeekState s, IEnumerable<int> scoreOrder)
    {
        ImmutableArray<HideSeekWireScore> scores = scoreOrder
            .Select(id => new HideSeekWireScore(id, ClampByte(s.ScoreOf(id))))
            .ToImmutableArray();

        HideSeekWireTally? tally = s.LastTally is { } card
            ? new HideSeekWireTally(card.RoundIndex, card.HiderPeerId,
                ClampByte(card.HiderGained), card.SeekerPeerId, ClampByte(card.SeekerGained),
                card.EndedByDisconnect,
                MatchOver: card.MatchOver,
                MatchIndex: card.MatchIndex,
                WinnerPeerId: card.WinnerPeerId,
                HiderTotal: ClampByte(card.HiderTotal),
                SeekerTotal: ClampByte(card.SeekerTotal))
            : null;

        return new HideSeekWire(
            Phase: (byte)s.Phase,
            Round: ClampUShort(s.RoundIndex),
            RemainingTenths: ClampUShort((int)MathF.Round(Math.Max(s.RemainingSec, 0f) * 10f)),
            HiderPeerId: s.HiderPeerId,
            SeekerPeerId: s.SeekerPeerId,
            Scores: scores,
            Refusal: (byte)s.Refusal,
            SortsCompleted: ClampUShort(s.SortsCompleted),
            // -1 rather than 0: tick 0 is a real tick, so there is no zero sentinel available.
            FoundTick: s.FoundTick is { } ft ? (int)Math.Clamp(ft, 0, int.MaxValue) : NoFoundTick,
            Tally: tally);
    }

    /// <summary>
    /// <b>Fold.</b> Rebuilds a complete local view from this message alone.
    /// <paramref name="previous"/> is accepted and then IGNORED for every field — deliberately: a
    /// real fold combines an accumulator with a new value, and the point being proven here is that
    /// this one does not need to. That is what "a late joiner is complete from one message" means
    /// as a testable property, and it is why the driver's late-join dump is the same message the
    /// live broadcast sends rather than a second code path.
    /// </summary>
    public static HideSeekView Fold(HideSeekView? previous, in HideSeekWire wire)
    {
        _ = previous; // deliberately unused — see the summary above.

        ImmutableDictionary<int, int> scores = wire.Scores.IsDefaultOrEmpty
            ? ImmutableDictionary<int, int>.Empty
            : wire.Scores.ToImmutableDictionary(r => r.PeerId, r => (int)r.Score);

        HideSeekTally? tally = wire.Tally is { } t
            ? new HideSeekTally(t.RoundIndex, t.HiderPeerId, t.HiderGained,
                t.SeekerPeerId, t.SeekerGained, t.EndedByDisconnect,
                MatchOver: t.MatchOver,
                MatchIndex: t.MatchIndex,
                WinnerPeerId: t.WinnerPeerId,
                HiderTotal: t.HiderTotal,
                SeekerTotal: t.SeekerTotal)
            : null;

        return new HideSeekView(
            (HideSeekPhase)wire.Phase,
            wire.Round,
            wire.RemainingTenths / 10f,
            wire.HiderPeerId,
            wire.SeekerPeerId,
            scores,
            (HideSeekRefusal)wire.Refusal,
            wire.SortsCompleted,
            wire.FoundTick,
            tally);
    }

    /// <summary>
    /// <b>Flattens this message into the primitives an RPC signature can carry</b>, and back again
    /// through <see cref="Unpack"/>. One place, so the marshalling is testable without a running
    /// scene tree — the whole reason this file has no <c>Godot</c> using directive.
    ///
    /// <para>The two score arrays are parallel by construction (built here, in one pass);
    /// <see cref="Unpack"/> walks only as far as the shorter of the two, so a truncated or
    /// malformed pair produces a short roster rather than an exception on a server's receive
    /// path.</para>
    /// </summary>
    public (byte Phase, int Round, int RemainingTenths, int Hider, int Seeker,
        int[] ScorePeers, int[] ScoreValues, byte Refusal, int Sorts, int FoundTick,
        int TallyRound, int TallyHider, int TallyHiderGain, int TallySeeker, int TallySeekerGain,
        bool TallyByDisconnect, bool TallyMatchOver, int TallyMatchIndex, int TallyWinner,
        int TallyHiderTotal, int TallySeekerTotal) Pack()
    {
        ImmutableArray<HideSeekWireScore> rows =
            Scores.IsDefault ? ImmutableArray<HideSeekWireScore>.Empty : Scores;
        var peers = new int[rows.Length];
        var values = new int[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            peers[i] = rows[i].PeerId;
            values[i] = rows[i].Score;
        }

        HideSeekWireTally card = Tally ?? new HideSeekWireTally(NoTallyRound, 0, 0, 0, 0, false);
        return (Phase, Round, RemainingTenths, HiderPeerId, SeekerPeerId, peers, values, Refusal,
            SortsCompleted, FoundTick,
            Tally is null ? NoTallyRound : card.RoundIndex, card.HiderPeerId,
            card.HiderGained, card.SeekerPeerId, card.SeekerGained, card.EndedByDisconnect,
            card.MatchOver, card.MatchIndex, card.WinnerPeerId, card.HiderTotal,
            card.SeekerTotal);
    }

    /// <summary>
    /// <b>Structural equality, written by hand, and it is not a nicety.</b> A record struct's
    /// synthesized <c>Equals</c> compares each member with that member's own equality — and
    /// <see cref="ImmutableArray{T}"/>'s is REFERENCE equality of the backing array. Two messages
    /// built one tick apart from identical state therefore compare UNEQUAL, silently. The driver
    /// decides whether to broadcast by asking "has the wire changed since the last one I sent", so
    /// the synthesized version would have made every single sim tick look like a change: a
    /// reliable RPC to every peer at 60 Hz, on a stream whose whole design is "absolute values,
    /// sent when something moves".
    ///
    /// <para>Found by a unit test that asserted <c>Fold(null, w) == Fold(old, w)</c> and failed
    /// while printing two identical-looking values. Left as a test as well as a fix.</para>
    /// </summary>
    public bool Equals(HideSeekWire other) =>
        Phase == other.Phase
        && Round == other.Round
        && RemainingTenths == other.RemainingTenths
        && HiderPeerId == other.HiderPeerId
        && SeekerPeerId == other.SeekerPeerId
        && Refusal == other.Refusal
        && SortsCompleted == other.SortsCompleted
        && FoundTick == other.FoundTick
        && Nullable.Equals(Tally, other.Tally)
        && SameScores(Scores, other.Scores);

    private static bool SameScores(ImmutableArray<HideSeekWireScore> a,
        ImmutableArray<HideSeekWireScore> b)
    {
        ImmutableArray<HideSeekWireScore> x =
            a.IsDefault ? ImmutableArray<HideSeekWireScore>.Empty : a;
        ImmutableArray<HideSeekWireScore> y =
            b.IsDefault ? ImmutableArray<HideSeekWireScore>.Empty : b;
        if (x.Length != y.Length)
            return false;
        for (int i = 0; i < x.Length; i++)
            if (!x[i].Equals(y[i]))
                return false;
        return true;
    }

    /// <inheritdoc cref="Equals(HideSeekWire)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phase);
        hash.Add(Round);
        hash.Add(RemainingTenths);
        hash.Add(HiderPeerId);
        hash.Add(SeekerPeerId);
        hash.Add(Refusal);
        hash.Add(SortsCompleted);
        hash.Add(FoundTick);
        hash.Add(Tally);
        if (!Scores.IsDefault)
            foreach (HideSeekWireScore row in Scores)
                hash.Add(row);
        return hash.ToHashCode();
    }

    /// <summary>The inverse of <see cref="Pack"/>. <paramref name="tallyRound"/> at
    /// <see cref="NoTallyRound"/> means the message carries no card.</summary>
    public static HideSeekWire Unpack(byte phase, int round, int remainingTenths, int hider,
        int seeker, int[]? scorePeers, int[]? scoreValues, byte refusal, int sorts, int foundTick,
        int tallyRound, int tallyHider, int tallyHiderGain, int tallySeeker, int tallySeekerGain,
        bool tallyByDisconnect, bool tallyMatchOver = false, int tallyMatchIndex = 0,
        int tallyWinner = 0, int tallyHiderTotal = 0, int tallySeekerTotal = 0)
    {
        int[] peers = scorePeers ?? Array.Empty<int>();
        int[] values = scoreValues ?? Array.Empty<int>();
        int n = Math.Min(peers.Length, values.Length);
        ImmutableArray<HideSeekWireScore>.Builder rows =
            ImmutableArray.CreateBuilder<HideSeekWireScore>(n);
        for (int i = 0; i < n; i++)
            rows.Add(new HideSeekWireScore(peers[i], ClampByte(values[i])));

        HideSeekWireTally? card = tallyRound == NoTallyRound
            ? null
            : new HideSeekWireTally(tallyRound, tallyHider, ClampByte(tallyHiderGain),
                tallySeeker, ClampByte(tallySeekerGain), tallyByDisconnect,
                MatchOver: tallyMatchOver,
                MatchIndex: Math.Max(tallyMatchIndex, 0),
                WinnerPeerId: tallyWinner,
                HiderTotal: ClampByte(tallyHiderTotal),
                SeekerTotal: ClampByte(tallySeekerTotal));

        return new HideSeekWire((byte)Math.Clamp((int)phase, 0, (int)HideSeekPhase.Tally),
            ClampUShort(round), ClampUShort(remainingTenths), hider, seeker,
            rows.ToImmutable(),
            (byte)Math.Clamp((int)refusal, 0, (int)HideSeekRefusal.NobodyCouldReachThat),
            ClampUShort(sorts), Math.Max(foundTick, NoFoundTick), card);
    }
}

/// <summary>A folded, display-ready view of one <see cref="HideSeekWire"/> — everything a client,
/// including a peer that joined five seconds ago, has to draw the HUD strip and the card.</summary>
public readonly record struct HideSeekView(
    HideSeekPhase Phase,
    int Round,
    float RemainingSec,
    int HiderPeerId,
    int SeekerPeerId,
    ImmutableDictionary<int, int> Scores,
    HideSeekRefusal Refusal,
    int SortsCompleted,
    int FoundTick,
    HideSeekTally? LastTally)
{
    /// <summary>This peer's cumulative score, or 0.</summary>
    public int ScoreOf(int peerId) =>
        Scores is not null && Scores.TryGetValue(peerId, out int v) ? v : 0;

    /// <summary><b>Structural equality, for the same reason <see cref="HideSeekWire"/> needs
    /// it</b> — an <see cref="ImmutableDictionary{TKey,TValue}"/> member compares by REFERENCE in
    /// a record's synthesized <c>Equals</c>, so two views folded from the same message would come
    /// out unequal. A client that diffs its view to decide whether to repaint the HUD would
    /// repaint every frame.</summary>
    public bool Equals(HideSeekView other)
    {
        if (Phase != other.Phase || Round != other.Round
            || !RemainingSec.Equals(other.RemainingSec)
            || HiderPeerId != other.HiderPeerId || SeekerPeerId != other.SeekerPeerId
            || Refusal != other.Refusal || SortsCompleted != other.SortsCompleted
            || FoundTick != other.FoundTick
            || !Nullable.Equals(LastTally, other.LastTally))
        {
            return false;
        }

        ImmutableDictionary<int, int> a = Scores ?? ImmutableDictionary<int, int>.Empty;
        ImmutableDictionary<int, int> b = other.Scores ?? ImmutableDictionary<int, int>.Empty;
        if (a.Count != b.Count)
            return false;
        foreach (KeyValuePair<int, int> row in a)
            if (!b.TryGetValue(row.Key, out int v) || v != row.Value)
                return false;
        return true;
    }

    /// <inheritdoc cref="Equals(HideSeekView)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phase);
        hash.Add(Round);
        hash.Add(RemainingSec);
        hash.Add(HiderPeerId);
        hash.Add(SeekerPeerId);
        hash.Add(Refusal);
        hash.Add(SortsCompleted);
        hash.Add(FoundTick);
        hash.Add(LastTally);
        // Order-independent, because a dictionary has none.
        if (Scores is not null)
            foreach (KeyValuePair<int, int> row in Scores)
                hash.Add(row.Key * 397 ^ row.Value);
        return hash.ToHashCode();
    }

    /// <summary>What this peer's role is, as the HUD says it. Empty when this peer has neither
    /// role — a third person in the room is a spectator, not a broken readout.</summary>
    public string RoleTextFor(int peerId) =>
        peerId != 0 && peerId == HiderPeerId ? "YOU HIDE"
        : peerId != 0 && peerId == SeekerPeerId ? "YOU SEEK"
        : string.Empty;
}
