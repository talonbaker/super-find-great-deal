using System;
using System.Collections.Generic;

namespace MpFoundation.Game.Round;

/// <summary>What the tally decided about one object settling in one bin.</summary>
public enum SortVerdict : byte
{
    /// <summary>Right bin, first time. The count went up by one.</summary>
    Right = 1,

    /// <summary>Wrong bin. <b>Nothing is subtracted</b> — the cost of a mistake is the time it
    /// took, which is the only currency this round has.</summary>
    Wrong = 2,
}

/// <summary>
/// One object observed inside one bin's volume for one step.
/// </summary>
/// <param name="PropId">The networked prop id, which is what the counted set is keyed on. Stable
/// across a round because the task room's sortables are AUTHORED and adopted
/// (<c>PropManager.AdoptAuthoredProps</c>), so they are never respawned with new ids.</param>
/// <param name="BinSlot">Which bin, 0..<see cref="SortRule.BinCount"/>-1.</param>
/// <param name="Item">The object's colour and shape.</param>
/// <param name="AtRest">Is it sitting there, as opposed to being held over the bin or still
/// falling into it? The settle timer only advances while this is true, and it going false is
/// what re-arms the wrong-bin buzz.</param>
public readonly record struct SortSighting(int PropId, int BinSlot, SortItemFacts Item, bool AtRest);

/// <summary>Something the tally decided this step, for the caller to make a noise about.</summary>
public readonly record struct SortEvent(int PropId, int BinSlot, SortVerdict Verdict);

/// <summary>
/// <b>The scoring rule for the task room</b> (TASK-1, 2026-09-19) — engine-free, so the one thing
/// the hider's whole score rests on is a unit test rather than a scene.
///
/// <para><b>What counts.</b> An object counts when it has been at rest inside a bin's volume for
/// <see cref="SettleSec"/> and that bin matches the round's rule. <b>Each object counts exactly
/// once</b> — a counted id is remembered for the round, so taking it back out subtracts nothing
/// and putting it back in adds nothing. Both halves of that are deliberate: subtracting would
/// make the last ten seconds of a round about defending a score instead of doing the job, and
/// re-counting would make a single object worth eighteen points to anybody who noticed.</para>
///
/// <para><b>Why a settle timer at all, rather than counting on contact.</b> The burst shoves
/// props (DOOR-1's impulse) and a carried object swings through a bin's mouth on the way past.
/// Counting the instant a volume is entered would score both of those. Half a second of
/// STILLNESS is the cheapest honest statement of "the player meant to put it there", and it is
/// the same shape as the rest-latch the prop layer already uses to decide a prop has
/// settled.</para>
///
/// <para><b>It is a class with state, and the state is the whole point</b> — the counted set and
/// the per-object dwell. <see cref="Step"/> is the only mutator and it returns what it decided,
/// so a caller never has to diff two snapshots to find out what happened. Same shape as
/// <c>HideSeekLoop.Step</c> one layer up.</para>
///
/// <para><b>Every peer runs one of these; only the server's is the score.</b> The task room's
/// objects and bins are authored identically everywhere and their transforms are replicated, so
/// each peer can derive the same verdicts and make the same noise without a packet — which is
/// how the buzz and the plate flash reach a client at all, since TASK-1 adds no wire field and no
/// channel. The SERVER's instance is the one wired into <c>IRoundFactSource.SortsCompleted</c>,
/// and that number is the only one anybody is scored on. See <c>SortRoom</c>.</para>
/// </summary>
public sealed class SortTally
{
    /// <summary>
    /// How long an object has to sit still in a bin before it counts, in seconds.
    ///
    /// <para>Half a second (the packet's number). Long enough that a prop swinging through a
    /// bin's mouth in a carried hand, or one shoved across the room by the burst, does not score;
    /// short enough that the hider never waits on the bin and can drop-and-turn at the pace the
    /// round wants. A player doing the job correctly never notices this number exists, which is
    /// the bar for a latch like this.</para>
    /// </summary>
    public const float SettleSec = 0.5f;

    /// <summary>Per-object progress toward the latch. An object that is not currently at rest in
    /// a bin is absent from this map entirely, which is what makes "it left" and "it never
    /// arrived" the same cheap case.</summary>
    private readonly Dictionary<int, Dwell> _dwell = new();

    /// <summary>Every object that has already scored this round. The whole of the once-only
    /// rule.</summary>
    private readonly HashSet<int> _counted = new();

    /// <summary>Reused across steps so a 60 Hz tick over eighteen objects allocates nothing.
    /// Cleared at the top of <see cref="Step"/>.</summary>
    private readonly List<SortEvent> _events = new();

    /// <summary>Scratch for "which tracked objects were NOT sighted this step", for the same
    /// reason.</summary>
    private readonly List<int> _gone = new();

    private readonly struct Dwell
    {
        public Dwell(int binSlot, float seconds, bool spoken)
        {
            BinSlot = binSlot;
            Seconds = seconds;
            Spoken = spoken;
        }

        /// <summary>Which bin this dwell is in. A move to a different bin restarts the clock:
        /// half a second in one bin and half in another is not half a second anywhere.</summary>
        public int BinSlot { get; }

        public float Seconds { get; }

        /// <summary>The latch has already fired for this stay. Cleared when the object leaves or
        /// is picked up, which is what re-arms a wrong-bin buzz for the next attempt — without
        /// it, one misplaced object buzzes sixty times a second.</summary>
        public bool Spoken { get; }
    }

    /// <summary>How many objects have been sorted correctly this round. <b>Absolute, never an
    /// increment</b> — <c>IRoundFactSource.SortsCompleted</c>'s contract.</summary>
    public int Completed => _counted.Count;

    /// <summary>Has this object already scored this round? Public so a fixture can assert the
    /// once-only rule from the outside rather than by counting sounds.</summary>
    public bool HasCounted(int propId) => _counted.Contains(propId);

    /// <summary>How many objects are currently part-way through the settle timer. Instrumentation
    /// only — it is what tells a log "the player is putting something down" apart from "nothing is
    /// happening".</summary>
    public int Settling => _dwell.Count;

    /// <summary>
    /// Advance by <paramref name="dt"/> against this step's sightings and return everything
    /// decided. The returned list is REUSED between calls: read it before the next
    /// <see cref="Step"/>, do not store it.
    /// </summary>
    /// <param name="rule">This round's rule, from <see cref="SortRule.For"/>. Passed in rather
    /// than held, because the rule belongs to the round and the tally belongs to the room —
    /// caching it here would be a second place for it to be stale at a reset edge.</param>
    /// <param name="sightings">Every object currently inside a bin volume, at rest or not. An
    /// object may appear at most once; if a level authors two overlapping bins, the first
    /// sighting wins and the second is ignored rather than fighting it every tick.</param>
    /// <param name="dt">Seconds since the last step. A negative, NaN or absurd value is treated
    /// as zero — a paused window and a resumed one both produce those, and neither should hand
    /// somebody a free sort.</param>
    public IReadOnlyList<SortEvent> Step(SortBy rule, IReadOnlyList<SortSighting>? sightings, float dt)
    {
        _events.Clear();
        if (float.IsNaN(dt) || dt < 0f)
            dt = 0f;

        // 1. Drop everything that is no longer resting in a bin. Done first, so an object that
        //    moved from bin A to bin B in one step is handled by the bin-change branch below
        //    rather than by being dropped and re-added with a fresh clock twice.
        _gone.Clear();
        foreach (KeyValuePair<int, Dwell> row in _dwell)
        {
            if (!StillResting(sightings, row.Key))
                _gone.Add(row.Key);
        }
        foreach (int id in _gone)
            _dwell.Remove(id);

        if (sightings is null)
            return _events;

        // 2. Advance every object that IS resting in a bin, and fire the latch at the threshold.
        for (int i = 0; i < sightings.Count; i++)
        {
            SortSighting s = sightings[i];
            if (!s.AtRest || s.BinSlot < 0 || s.BinSlot >= SortRule.BinCount)
                continue;

            if (!_dwell.TryGetValue(s.PropId, out Dwell d))
            {
                _dwell[s.PropId] = new Dwell(s.BinSlot, dt, spoken: false);
                // A duplicate sighting of the same prop later in the list falls into the branch
                // below and adds a second dt; that is the "first sighting wins" rule failing
                // open by a frame, which is cheaper than scanning for duplicates every tick.
                continue;
            }

            if (d.BinSlot != s.BinSlot)
            {
                // Moved bins. The clock restarts and the latch re-arms: a player dragging an
                // object along a row of bins must not bank a sort in the bin they passed over.
                _dwell[s.PropId] = new Dwell(s.BinSlot, dt, spoken: false);
                continue;
            }

            float seconds = d.Seconds + dt;
            bool spoken = d.Spoken;
            if (!spoken && seconds >= SettleSec)
            {
                spoken = true;
                // An object that has already scored is DONE for the round: it neither scores
                // again nor buzzes if it ends up somewhere wrong afterwards. Saying anything
                // here would be the game commenting on a decision that no longer costs or earns
                // the player anything.
                if (!_counted.Contains(s.PropId))
                {
                    if (SortRule.Matches(rule, s.Item, s.BinSlot))
                    {
                        _counted.Add(s.PropId);
                        _events.Add(new SortEvent(s.PropId, s.BinSlot, SortVerdict.Right));
                    }
                    else
                    {
                        _events.Add(new SortEvent(s.PropId, s.BinSlot, SortVerdict.Wrong));
                    }
                }
            }

            _dwell[s.PropId] = new Dwell(s.BinSlot, seconds, spoken);
        }

        return _events;
    }

    /// <summary>Back to the start of a round: nothing counted, nothing settling. Called on
    /// <c>HideSeekDriver.ResetRequested</c>, the same edge that sends the objects home.</summary>
    public void Reset()
    {
        _dwell.Clear();
        _counted.Clear();
        _events.Clear();
    }

    private static bool StillResting(IReadOnlyList<SortSighting>? sightings, int propId)
    {
        if (sightings is null)
            return false;
        for (int i = 0; i < sightings.Count; i++)
            if (sightings[i].PropId == propId && sightings[i].AtRest)
                return true;
        return false;
    }
}
