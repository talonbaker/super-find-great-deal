using System;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>What a sortable object is</b>, in the two ways that matter: a colour and a shape, and the
/// bins only ever ask for one of them.
///
/// <para>Talon, 2026-09-19: <i>"object sort either by color or shape, different bins, so the
/// player has to concentrate and get focused."</i> That sentence is the whole design and this
/// enum pair is the whole of its data. A red ball and a red cube belong together under
/// <see cref="SortBy.Colour"/>; the red ball and the BLUE ball belong together under
/// <see cref="SortBy.Shape"/>. Every object is therefore a member of two different groups at
/// once, which is what makes the job need eyes and hands rather than habit.</para>
///
/// <para><b>Ordinals are pinned and matter</b>: they ARE the bin index. Bin 0 is
/// <see cref="SortColour.Red"/> under a colour round and <see cref="SortShape.Cube"/> under a
/// shape round, and <see cref="SortRule.Matches"/> is that sentence and nothing else. Renumbering
/// either enum silently repoints every bin in the task room.</para>
///
/// <para><b>Not on the wire.</b> Every peer has the same authored task room, so every peer
/// already knows which object is which — sending it would be sending the level. The only thing
/// the round replicates is the COUNT (<c>HideSeekView.SortsCompleted</c>), and the RULE is
/// derived from the round index on every peer by <see cref="SortRule.For"/>.</para>
/// </summary>
public enum SortColour : byte
{
    /// <summary>Bin 0 under a colour round.</summary>
    Red = 0,

    /// <summary>Bin 1 under a colour round.</summary>
    Blue = 1,

    /// <summary>Bin 2 under a colour round.</summary>
    Yellow = 2,
}

/// <inheritdoc cref="SortColour"/>
public enum SortShape : byte
{
    /// <summary>Bin 0 under a shape round. A 0.15 m cube.</summary>
    Cube = 0,

    /// <summary>Bin 1 under a shape round. An 0.08 m-radius ball.</summary>
    Ball = 1,

    /// <summary>Bin 2 under a shape round. A 0.05 m-radius, 0.14 m-tall can.</summary>
    Can = 2,
}

/// <summary>Which of an object's two properties this round's bins are asking about.</summary>
public enum SortBy : byte
{
    /// <summary>Odd rounds. The plates read RED / BLUE / YELLOW and are tinted to match.</summary>
    Colour = 0,

    /// <summary>Even rounds. The plates read CUBE / BALL / CAN with a small icon of the shape
    /// above the word.</summary>
    Shape = 1,
}

/// <summary>One sortable object's two facts, as the rule sees it.</summary>
/// <param name="Colour">Authored in the task room — see <c>SortItem</c> for how a scene says it
/// without tripping this build's nested-export trap.</param>
/// <param name="Shape">Derived from the object's own collider, the way every other shape fact in
/// this repo is (<c>Carryable.ShapeFromCollider</c>).</param>
public readonly record struct SortItemFacts(SortColour Colour, SortShape Shape);

/// <summary>
/// <b>The rule of the round, derived rather than sent</b> (TASK-1, 2026-09-19).
///
/// <para>Engine-free and static, for the reason every rule in <c>scripts/game/round/</c> is: the
/// server, three clients, the bin plates, the HUD strip and the unit suite all have to agree
/// about which bin a red ball belongs in, and the only way five readers agree by construction is
/// if there is one function and it needs no scene tree to run.</para>
///
/// <para><b>Nothing here is on the wire and nothing here needs to be.</b> The round index already
/// rides <c>HideSeekWire</c>, every peer has the same authored room, and
/// <see cref="For"/> is a pure function of the index — so a peer that has heard one round message
/// knows the rule, the bins' meanings and the plates' words with no further packet. That is also
/// why a late joiner's plates are right on the first frame they are visible.</para>
/// </summary>
public static class SortRule
{
    /// <summary>How many bins the task room has, and therefore how many members each of
    /// <see cref="SortColour"/> and <see cref="SortShape"/> must carry. Three is not a coincidence
    /// — it is the shared cardinality that lets one set of bins serve both rules.</summary>
    public const int BinCount = 3;

    /// <summary>
    /// <b>Which property this round sorts by.</b> Round 1 sorts by colour, round 2 by shape,
    /// round 3 by colour again.
    ///
    /// <para><b>Alternating rather than random, and that is the point of the mechanic.</b> A
    /// match is two rounds and each player hides once (<c>HideSeekTuning.MatchRounds</c>), so
    /// this guarantees each player does the job once each way — and the second time is the hard
    /// one, because the room looks identical and the habit the first round built is now
    /// wrong.</para>
    ///
    /// <para>A round index below 1 is read as round 1. The loop's index is 1-based and clamps its
    /// own floor, so that case is a defaulted struct rather than a real round; answering
    /// <see cref="SortBy.Colour"/> there is the same answer the first real round gets, which is
    /// the one that cannot surprise anybody.</para>
    /// </summary>
    public static SortBy For(int round) => Math.Max(round, 1) % 2 == 1 ? SortBy.Colour : SortBy.Shape;

    /// <summary>
    /// <b>Does this object belong in this bin, under this rule?</b> The one comparison, in one
    /// place.
    ///
    /// <para>A bin is an INDEX, not a colour and not a shape: bin 0 means
    /// <see cref="SortColour.Red"/> on an odd round and <see cref="SortShape.Cube"/> on an even
    /// one, and the bins themselves never change. That is why the three plates re-label at the
    /// reset edge instead of the room re-arranging itself — the player learns three positions
    /// once and then has to re-learn what they mean, which is the concentration the mechanic is
    /// asking for.</para>
    ///
    /// <para>An out-of-range slot is <c>false</c> rather than an exception: the caller is a
    /// physics overlap test in a room a level author can edit, and a fourth bin authored by
    /// mistake should sort nothing, not crash a server.</para>
    /// </summary>
    public static bool Matches(SortBy rule, in SortItemFacts item, int binSlot)
    {
        if (binSlot < 0 || binSlot >= BinCount)
            return false;
        return rule == SortBy.Colour
            ? (int)item.Colour == binSlot
            : (int)item.Shape == binSlot;
    }

    /// <summary>The one bin this object belongs in under <paramref name="rule"/>. The inverse of
    /// <see cref="Matches"/>, and it exists so a fixture (or a bot script) can ask "where does
    /// this go" without re-deriving the mapping and getting it subtly different.</summary>
    public static int BinFor(SortBy rule, in SortItemFacts item) =>
        rule == SortBy.Colour ? (int)item.Colour : (int)item.Shape;
}
