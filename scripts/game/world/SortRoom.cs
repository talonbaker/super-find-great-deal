using System.Collections.Generic;
using System.Text;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Round;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>The task room's job, wired up</b> (TASK-1, 2026-09-19): one poll that watches eighteen
/// authored objects against three authored bins, steps <see cref="SortTally"/>, and answers
/// <see cref="IRoundFactSource.SortsCompleted"/> with what the hider has got done.
///
/// <para>Talon, 2026-09-19: <i>"object sort either by color or shape, different bins, so the
/// player has to concentrate and get focused."</i> Everything that DECIDES anything lives in
/// <see cref="SortRule"/> and <see cref="SortTally"/>, which are engine-free and unit-tested;
/// this class is the adapter that turns a scene into their inputs. That split is the whole
/// reason the hider's score is testable at all.</para>
///
/// <para><b>Every peer runs one and only the server's is the score.</b> TASK-1 adds no wire
/// field, no RPC and no channel — the count rides <c>HideSeekWire</c>'s existing slot, which is
/// exactly what the rename in this lane's first commit was about. So the BUZZ and the PLATE
/// FLASH, which have to happen on every peer, are derived locally: the objects and bins are
/// authored identically everywhere and their transforms are replicated, so each peer reaches the
/// same verdicts from the same facts a beat apart. The SERVER's tally is the one registered with
/// <c>HideSeekDriver</c>, and that number is the only one anybody is scored on. A client's copy
/// can at worst make one extra noise; it can never change a score.</para>
///
/// <para><b>Rest is read from the replicated transform, not from the registry.</b> A client has
/// no <c>PropRegistry</c>, so "is it sitting there" is <c>HolderPeerId == 0</c> plus "it has not
/// moved since the last poll" — one rule on every peer rather than an authoritative branch and a
/// cosmetic one that could disagree. <see cref="SortTally.SettleSec"/> on top of it is what makes
/// a half-second of stillness mean the player meant it.</para>
///
/// <para><b>Counting only runs in <see cref="HideSeekPhase.Seeking"/>.</b> Program §5: progress
/// is frozen at the Found tick. The loop freezes its own copy (<c>SortsAtFound</c>), and this
/// stops feeding it, so the number on the strip and on the wall stops moving at the instant the
/// door goes — including for a hider who keeps sorting out of momentum while the seeker walks
/// in. Nothing special happens here at the burst: DOOR-1's forced drop and impulse are its own,
/// and an object the burst throws into a bin cannot score because it is not at rest.</para>
///
/// <para><b>It builds nothing and it has no <c>[Export]</c>.</b> Bins and objects are found by
/// walking the room, identified by NODE NAME and COLLIDER — the two native facts that survive
/// this build's nested-instance trap (CARRY-1 §6, CLOCK-1 §3.1).</para>
/// </summary>
public partial class SortRoom : Node, IRoundFactSource
{
    /// <summary>The node name in <c>TaskRoom.tscn</c>.</summary>
    public const string NodeName = "SortRoom";

    /// <summary>The prefix every line this feature prints carries, so a runner can grep for it
    /// without matching <c>[door]</c>, <c>[clock]</c> or <c>[round]</c>.</summary>
    public const string LogPrefix = "[sort]";

    /// <summary>How far an object may drift between two polls and still count as at rest, in
    /// metres. At the 60 Hz physics tick this is 0.3 m/s — well under the speed a settling prop
    /// still has, and far under a carried one, while being loose enough that a frozen kinematic
    /// body's floating-point jitter never reads as motion.</summary>
    public const float RestEpsilonM = 0.005f;

    /// <summary>Volume of a sort cue, dB. Quiet: it fires up to eighteen times a round from a
    /// bin the player is standing at, so it is a tick rather than an event.</summary>
    private const float CueVolumeDb = -8f;

    /// <summary>How far a sort cue carries. The bins are against one wall of a 10 m room and the
    /// only person who should hear this is the person at them; the seeker is two rooms away in
    /// any case.</summary>
    private const float CueMaxDistanceM = 14f;

    private readonly SortTally _tally = new();
    private readonly List<SortBin> _bins = new();
    private readonly List<SortItem> _items = new();
    private readonly List<SortSighting> _sightings = new();
    private readonly Dictionary<int, Vector3> _lastSeenAt = new();

    private Node? _room;
    private HideSeekDriver? _driver;
    private bool _registered;
    private bool _announced;
    private bool _silent;
    private bool _isServer;
    private bool _serverKnown;

    // --- IRoundFactSource ---------------------------------------------------------------------
    //
    // Exactly one fact, and it is ABSOLUTE (the interface's contract). Everything else is false
    // or null, which is what RoundFacts.Combine's OR and first-non-null rules are built for: a
    // source that does not know a fact can never veto one that does, so this can be registered
    // beside BTN-1's buttons and REACH-1's audit without any of the three knowing about the
    // others.

    /// <inheritdoc/>
    public int SortsCompleted => _tally.Completed;

    /// <inheritdoc/>
    public bool HostPressedStart => false;

    /// <inheritdoc/>
    public bool HiderHeldRackProp => false;

    /// <inheritdoc/>
    public bool HiderPressedConfirm => false;

    /// <inheritdoc/>
    public bool HiderHoldsTarget => false;

    /// <inheritdoc/>
    public bool? TargetRetrievable => null;

    /// <inheritdoc/>
    public bool TargetInDropOff => false;

    /// <inheritdoc/>
    public bool AnyPressedEnd => false;

    /// <inheritdoc/>
    /// <remarks>Nothing to clear: this source carries no edge. Every fact it answers is a level
    /// read fresh from the tally each tick, which is precisely what the interface means by
    /// "absolute, never an increment".</remarks>
    public void AfterStep() { }

    // --- lifecycle ----------------------------------------------------------------------------

    public override void _Ready()
    {
        _room = GetParent();
        if (_room == null)
        {
            GD.PushError($"{LogPrefix} {NodeName} has no parent room — it is authored as a child "
                         + "of TaskRoom.tscn's root and finds its bins and objects there.");
            return;
        }

        foreach (Node child in _room.GetChildren())
        {
            if (child is SortBin bin)
                _bins.Add(bin);
        }
        _bins.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        CollectItems(_room, _items);

        _silent = NetworkManager.Instance?.IsHeadless == true;
    }

    public override void _ExitTree() => Unsubscribe();

    private static void CollectItems(Node from, List<SortItem> into)
    {
        foreach (Node child in from.GetChildren())
        {
            if (child is SortItem item)
                into.Add(item);
            CollectItems(child, into);
        }
    }

    /// <summary>
    /// The one poll, at the physics rate.
    ///
    /// <para><b>Physics rather than render</b>, unlike <c>BurstDoor</c>'s tween and
    /// <c>RoundAudio</c>'s 10 Hz fan-out: this reads prop transforms, which the physics server
    /// moves, and the settle latch is a statement about how long something has been still. A
    /// latch stepped on a variable render clock would take a different amount of real time on a
    /// 60 Hz machine and a 144 Hz one, which is a scoring rule that depends on somebody's
    /// monitor.</para>
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!_registered)
            TrySubscribe();
        if (_driver == null)
            return;

        SortBy rule = SortRule.For(RoundIndex());
        foreach (SortBin bin in _bins)
        {
            bin.Relabel(rule);
            bin.Tick(delta);
        }

        Announce(rule);

        if (Phase() != HideSeekPhase.Seeking)
            return;

        BuildSightings();
        IReadOnlyList<SortEvent> events = _tally.Step(rule, _sightings, (float)delta);
        for (int i = 0; i < events.Count; i++)
            Report(events[i], rule);
    }

    /// <summary>
    /// <c>Gameplay</c> builds the world BEFORE it builds the driver, and
    /// <c>SupermarketWorldSelfTest</c> instantiates the room with no driver at all — so this
    /// polls rather than reaching for the driver in <c>_Ready</c>. One null check a tick, and
    /// neither case needs a special path. Exactly <c>BurstDoor.TrySubscribe</c>'s shape.
    ///
    /// <para><b>Registered on every peer, not only the server.</b> <c>HideSeekDriver</c> folds
    /// facts inside a server-gated <c>_PhysicsProcess</c>, so a client's registration costs one
    /// list entry and nothing else — and doing it unconditionally means there is no "is this the
    /// server" branch anywhere in this file's wiring to get wrong.</para>
    /// </summary>
    private void TrySubscribe()
    {
        HideSeekDriver? driver = HideSeekDriver.Instance;
        if (driver == null)
            return;
        _driver = driver;
        driver.Register(this);
        driver.ResetRequested += OnResetRequested;
        _registered = true;
    }

    private void Unsubscribe()
    {
        if (!_registered || _driver == null)
            return;
        _driver.ResetRequested -= OnResetRequested;
        _registered = false;
        _driver = null;
    }

    /// <summary>
    /// The round boundary: nothing counted, no settle clocks, and the plates re-label on the
    /// next poll for the new round's rule.
    ///
    /// <para><b>The objects go home for free and that is deliberate.</b>
    /// <c>Gameplay.OnRoundResetRequested</c> already fans <c>PropManager.ResetForNewPlaythrough</c>
    /// on this same edge, and an adopted authored prop's restore is "return to its authored
    /// transform" — which for these eighteen IS their place in the supply crate. A second
    /// mechanism here would be a second opinion about where a prop lives.</para>
    ///
    /// <para>Fires on every peer (the driver raises it from the applied message), so every
    /// peer's tally clears at the same instant its objects go home.</para>
    /// </summary>
    private void OnResetRequested()
    {
        _tally.Reset();
        _lastSeenAt.Clear();
        GD.Print($"{LogPrefix} reset: count cleared, plates relabel for round "
                 + $"{RoundIndex()} ({HideSeekText.SortRuleWord(SortRule.For(RoundIndex()))}) "
                 + $"peer={Multiplayer.GetUniqueId()}");
    }

    // --- reading the room ---------------------------------------------------------------------

    private int RoundIndex()
    {
        if (_driver == null)
            return 1;
        if (IsServer())
            return _driver.ServerState.RoundIndex;
        return _driver.Synced ? _driver.View.Round : 1;
    }

    private HideSeekPhase Phase()
    {
        if (_driver == null)
            return HideSeekPhase.Holding;
        if (IsServer())
            return _driver.ServerState.Phase;
        return _driver.Synced ? _driver.View.Phase : HideSeekPhase.Holding;
    }

    /// <summary>DOOR-1's test, verbatim. Cached once a peer exists, because the answer cannot
    /// change inside a session and this is asked twice a tick.</summary>
    private bool IsServer()
    {
        if (_serverKnown)
            return _isServer;
        if (!Multiplayer.HasMultiplayerPeer())
            return false;
        _isServer = Multiplayer.IsServer();
        _serverKnown = true;
        return _isServer;
    }

    /// <summary>
    /// Every object currently inside a bin's mouth, and whether it is sitting still there.
    ///
    /// <para>Eighteen objects against three bins is 54 point-in-box tests a tick — one
    /// multiply-add-compare each against a cached inverse — which is cheaper than the single
    /// shape query REACH-1 costed at 46/s, and it needs the physics server not at all.</para>
    /// </summary>
    private void BuildSightings()
    {
        _sightings.Clear();
        foreach (SortItem item in _items)
        {
            NetworkedProp? prop = item.Prop;
            Carryable? body = prop?.Body;
            if (prop == null || body == null || prop.PropId == 0)
                continue;

            Vector3 at = body.GlobalPosition;
            bool still = _lastSeenAt.TryGetValue(prop.PropId, out Vector3 was)
                         && at.DistanceSquaredTo(was) <= RestEpsilonM * RestEpsilonM;
            _lastSeenAt[prop.PropId] = at;

            int slot = -1;
            foreach (SortBin bin in _bins)
            {
                if (bin.Slot >= 0 && bin.Contains(at))
                {
                    slot = bin.Slot;
                    break;
                }
            }
            if (slot < 0)
                continue;

            _sightings.Add(new SortSighting(prop.PropId, slot, item.Facts,
                AtRest: still && prop.HolderPeerId == 0));
        }
    }

    // --- saying so ------------------------------------------------------------------------------

    /// <summary>
    /// One verdict: the plate, the sound and the line.
    ///
    /// <para><b>The line is printed on every peer with its own peer id in it</b>, because "the
    /// host scored it and the client never heard" and "both agreed" are the same absence in a
    /// log that only the server writes — and this feature's whole correctness claim is that
    /// every peer reaches the same verdict from replicated state. <c>tests/Run-SortTest.ps1</c>
    /// reads these.</para>
    /// </summary>
    private void Report(in SortEvent e, SortBy rule)
    {
        bool right = e.Verdict == SortVerdict.Right;
        SortBin? bin = BinAt(e.BinSlot);
        if (right)
            bin?.Flash();

        GD.Print($"{LogPrefix} {(right ? "sort-good" : "sort-bad")} prop={e.PropId} "
                 + $"bin={e.BinSlot} ({HideSeekText.BinPlateWord(rule, e.BinSlot)}) "
                 + $"rule={HideSeekText.SortRuleWord(rule)} total={_tally.Completed} "
                 + $"peer={Multiplayer.GetUniqueId()}");

        if (_silent || bin == null)
            return;
        SfxLab.PlayStream3D(bin, bin.GlobalPosition,
            SfxLab.Get(right ? Sfx.SortGood : Sfx.SortBad),
            CueVolumeDb, pitchJitter: 0.03f, maxDistance: CueMaxDistanceM);
    }

    private SortBin? BinAt(int slot)
    {
        foreach (SortBin bin in _bins)
            if (bin.Slot == slot)
                return bin;
        return null;
    }

    /// <summary>
    /// The census, once, on the first poll after the props have been adopted.
    ///
    /// <para><b>It exists because the two facts it reports are DERIVED</b> — the colour from a
    /// node name and the shape from a collider — and a derivation that silently answered the
    /// default for every object would produce a room of eighteen red cubes that looks perfectly
    /// normal from a diff. This line is what a suite asserts the 3 x 3 x 2 census against, and
    /// it is the instrument that would have caught this build's nested-export trap on the first
    /// run rather than on the first playtest.</para>
    /// </summary>
    private void Announce(SortBy rule)
    {
        if (_announced || _items.Count == 0)
            return;
        // Adoption runs after the world is in the tree, so ids are 0 for the first frame or two;
        // waiting for them means the census line names real prop ids a suite can cross-check.
        foreach (SortItem item in _items)
            if (item.PropId == 0)
                return;
        _announced = true;

        var byColour = new int[SortRule.BinCount];
        var byShape = new int[SortRule.BinCount];
        int unresolved = 0;
        var line = new StringBuilder();
        foreach (SortItem item in _items)
        {
            byColour[(int)item.Colour]++;
            byShape[(int)item.Shape]++;
            if (!item.Resolved)
                unresolved++;
            if (line.Length > 0)
                line.Append(' ');
            line.Append($"{item.PropId}={item.Colour}/{item.Shape}");
        }

        GD.Print($"{LogPrefix} task room ready: {_items.Count} sortable(s), {_bins.Count} bin(s), "
                 + $"colours red={byColour[0]} blue={byColour[1]} yellow={byColour[2]}, "
                 + $"shapes cube={byShape[0]} ball={byShape[1]} can={byShape[2]}, "
                 + $"unresolved={unresolved}, rule={HideSeekText.SortRuleWord(rule)}, "
                 + $"peer={Multiplayer.GetUniqueId()}");
        GD.Print($"{LogPrefix} items: {line}");
    }
}
