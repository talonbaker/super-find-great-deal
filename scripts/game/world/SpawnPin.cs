using System.Collections.Generic;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>Which spawn marker a NAMED peer gets, regardless of the order it joins in.</b> The
/// engine-free half of <c>--spawn-index</c> (INT-1, 2026-09-19, packet ruling 6), so the mapping
/// can be asserted in the Godot-free unit suite and the adapter in <c>Gameplay</c> stays a
/// handful of lines.
///
/// <para><b>The defect it exists for is measured, not imagined.</b> SHELF-1 §6.3:
/// <i>"spawn markers are dealt by JOIN ORDER, which races … measured across three runs of
/// Run-MaterialSfxTest, the box bot drew marker 2 once and marker 1 the next time, and the run
/// where it drew the far marker went red on a pickup that never happened."</i> That was harmless
/// while the search room was an empty box, because every marker could see every fixture. It is
/// not harmless now: the dressed room is four 9.2 m corridors with no cross-aisle, and a
/// straight-line walk brain in the wrong walkway stops dead at a shelf face. So a suite that
/// stages a fixture "in the walkway its bot spawns in" is betting on a race it cannot see.</para>
///
/// <para><b>A name, not a join slot, and that is the whole point.</b> Re-ordering which marker
/// the n-th joiner gets would just relabel the same race. What a suite actually knows is which
/// BOT it wants where — it launched the process and gave it <c>--name</c> — so the pin is keyed
/// on that.</para>
///
/// <para><b>Server-side, and it takes nothing new onto any wire.</b> The flag is read by the
/// server; the name it matches on is <c>SandboxAvatar.DisplayName</c>, which already replicates
/// from the owning client through the existing <c>Sync</c> synchronizer. So there is no new
/// message, no new channel, no <c>ProtocolVersion</c> bump, and nothing a client can ask for:
/// a peer cannot pin itself, it can only be pinned by the process that started the server.
/// An ordinary session passes no flag, <see cref="Empty"/> answers "not pinned" for every name,
/// and the spawn path is byte-for-byte what it was.</para>
///
/// <para><b>Matching is exact and case-sensitive.</b> A suite writes both sides of this — the
/// <c>--name</c> and the <c>--spawn-index</c> — so a near-match is a typo, and a typo that
/// silently half-worked is how a suite starts passing for the wrong reason. A name that is not
/// in the map is simply not pinned and keeps the ordinary join-order deal.</para>
/// </summary>
public sealed class SpawnPin
{
    /// <summary>No pins at all: what every session that does not pass the flag uses. Shared
    /// because it is immutable in practice and a null check at each call site is one more thing
    /// to get wrong.</summary>
    public static readonly SpawnPin Empty = new(new Dictionary<string, int>());

    private readonly Dictionary<string, int> _byName;

    private SpawnPin(Dictionary<string, int> byName) => _byName = byName;

    /// <summary>How many names are pinned. Zero for <see cref="Empty"/> and for a spec that
    /// parsed to nothing usable.</summary>
    public int Count => _byName.Count;

    /// <summary>
    /// Parses <c>"BotA=2,BotB=0"</c>. Returns <see cref="Empty"/> for null, blank or wholly
    /// unusable input; skips any individual entry that is malformed or negative and keeps the
    /// rest.
    ///
    /// <para><b>Tolerant per entry rather than all-or-nothing, and the reason is which failure
    /// is louder.</b> A spec that threw would take a whole suite down at launch for one typo,
    /// with the error a long way from the flag. A spec that drops one bad entry leaves that bot
    /// on the ordinary join-order deal, which is exactly the behaviour the suite had before this
    /// flag existed — and <see cref="Count"/> plus the server's own log line say how many
    /// actually landed, so "my pin did nothing" is answerable from the log rather than guessable.
    /// A negative index is dropped for the same reason an unparseable one is: there is no marker
    /// there and wrapping it would silently pin the bot somewhere nobody asked for.</para>
    ///
    /// <para><b>An index is NOT range-checked here.</b> How many markers a room has is a fact
    /// about the world, which this type has no access to and should not: the caller already
    /// wraps with <c>index % count</c> for late joiners, and the same wrap applies to a pin, so
    /// the two paths cannot disagree about what marker 5 of 4 means.</para>
    /// </summary>
    public static SpawnPin Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return Empty;
        var map = new Dictionary<string, int>();
        foreach (string entry in spec.Split(','))
        {
            string trimmed = entry.Trim();
            if (trimmed.Length == 0)
                continue;
            int eq = trimmed.IndexOf('=');
            if (eq <= 0 || eq == trimmed.Length - 1)
                continue;
            string name = trimmed[..eq].Trim();
            if (name.Length == 0)
                continue;
            if (!int.TryParse(trimmed[(eq + 1)..].Trim(), out int index) || index < 0)
                continue;
            // Last one wins, so a spec that names a bot twice is not an error and not a silent
            // half-application: the reader's eye takes the last assignment in a list too.
            map[name] = index;
        }
        return map.Count == 0 ? Empty : new SpawnPin(map);
    }

    /// <summary>The marker index pinned for <paramref name="displayName"/>, or −1 for "not
    /// pinned, use the ordinary join-order deal". −1 rather than a nullable because every caller
    /// is comparing against an index anyway and a sentinel keeps the adapter to one branch.
    ///
    /// <para>A blank or null name answers −1 too: an avatar whose <c>DisplayName</c> has not
    /// replicated yet is not "unpinned", it is "not known yet", and the caller re-asks on the
    /// next tick rather than pinning it to whatever an empty string happened to match.</para>
    /// </summary>
    public int IndexFor(string? displayName) =>
        !string.IsNullOrEmpty(displayName) && _byName.TryGetValue(displayName, out int index)
            ? index
            : -1;

    /// <summary>The pinned names, for a log line. Order is unspecified; callers that print it
    /// sort.</summary>
    public IEnumerable<KeyValuePair<string, int>> Pins => _byName;

    /// <summary>How close another body has to be to a pinned marker for the pin to WAIT for it
    /// (PHYS-2 finding 2, fixed by INT-2 part B, 2026-09-21). A `SearchSpawn` marker's nearest
    /// neighbour is 2.1 m away, so at 1 m this can only ever mean "somebody is standing ON this
    /// marker" and never "somebody is standing at the next one".</summary>
    public const float PinClearM = 1.0f;

    /// <summary>
    /// <b>Is a peer that has not been settled yet standing where a pin wants to land?</b>
    ///
    /// <para><b>The defect, measured.</b> PHYS-2 §2.1: <c>Gameplay.ApplySpawnPins</c> pins a name
    /// the frame it replicates, and names replicate at different times. <c>SfxProduceBot</c>'s
    /// arrived first and it was teleported onto marker 3 <i>while the still-unnamed witness was
    /// standing on it by join order</i> — the produce bot ended up at <c>y = 2.30</c>, on the
    /// witness's head, for 0.6 s, then fell into the +Z walkway and stopped 5.7 m from its prop
    /// with <c>heldPropId = -1</c> all run. <c>Run-MaterialSfxTest</c> 2/3, and the suite
    /// correctly reported it as a sound that never played.</para>
    ///
    /// <para><b>Why the fix is the ORDER and not the radii or the table.</b> The search room has
    /// exactly four <c>SearchSpawn</c> markers and that suite pins names to all four, so there is
    /// no unreserved marker to deal an unnamed joiner instead — "reserve the pinned markers from
    /// join-order assignment" is unavailable here by construction. What is always available is to
    /// let the occupant move first: a peer whose name has not replicated yet is one the poll is
    /// still going to act on, so a pin that would land on it simply waits a frame.</para>
    ///
    /// <para><b>It cannot deadlock</b>, and that is why the test is "has no name yet" rather than
    /// "is not pinned yet". Every avatar whose name HAS arrived is settled in the same pass it is
    /// seen — pinned, or written off as unpinned — so the waiting set strictly shrinks. Two peers
    /// cannot wait on each other. A peer that never publishes a name at all would hold a pin
    /// forever, which is a session that never passes <c>--spawn-index</c>: the flag is the
    /// server's, every driver that uses it also passes <c>--name</c>, and
    /// <see cref="Empty"/> short-circuits the whole path in every session a player starts.</para>
    ///
    /// <para>Static and engine-free so the geometry is asserted in the Godot-free suite and the
    /// adapter in <c>Gameplay</c> stays a handful of lines, which is this whole file's pattern.
    /// </para>
    /// </summary>
    public static bool BlockedByAnUnsettledPeer(
        Godot.Vector3 destination, IEnumerable<Godot.Vector3>? unsettledAt, float clearM)
    {
        if (unsettledAt == null)
            return false;
        float r2 = clearM * clearM;
        foreach (Godot.Vector3 at in unsettledAt)
        {
            if (at.DistanceSquaredTo(destination) <= r2)
                return true;
        }
        return false;
    }
}
