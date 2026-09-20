using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The shelf of near-misses in the holding room.</b> Three objects, a shape apart, the same
/// colour and the same size class. The hider takes one; that one becomes the target; the other
/// two stay on the shelf so the SEEKER can walk up and see what they are looking for. Both
/// players knowing the object is the brief, and this shelf is where they learn it.
///
/// <para><b>This class is a LOOKUP, not a mechanism.</b> It answers one question — "is prop id N
/// one of my three" — and it answers it identically on every peer, which is the whole reason it
/// exists as a node rather than as a list in <c>RoundControls</c>: the three objects are
/// authored <c>NetworkedProp</c> children of this node, <c>PropManager.AdoptAuthoredProps</c>
/// gives them ids by sorting on node path, and every peer instanced the same scene. So a client
/// can derive "the hider is holding something off the rack" — which is what lights the START
/// lamp — with no new field on the wire.</para>
///
/// <para><b>It builds nothing.</b> The shelf and all three objects are authored in
/// <c>HoldingRoom.tscn</c> (<c>.claude/rules/godot-scenes.md</c>, and
/// <c>SupermarketWorldSelfTest</c> counts the room's packed nodes against its live ones).
/// SHELF-1 replaces the three <c>scenes/game/props/Deal*.tscn</c> files in place; nothing here
/// or in the room scene changes when it does.</para>
/// </summary>
public partial class ObjectRack : Node3D
{
    /// <summary>The group the rack joins, so a level author can move it without anybody's code
    /// learning a path. (<c>RoundControls</c> finds it by type; the group is for a suite, a
    /// capture script and a human opening the scene.)</summary>
    public const string Group = "object_rack";

    /// <summary>How many objects the rack is supposed to carry. Three near-misses: a shape apart
    /// is the whole game (program §0), two would make it a coin flip and four would make the
    /// seeker's glance at the shelf useless.</summary>
    public const int ExpectedObjects = 3;

    private readonly List<int> _propIds = new();
    private readonly List<NetworkedProp> _props = new();
    private bool _resolved;

    /// <summary>The three adopted prop ids, in node order. Empty until the props have been
    /// adopted; resolved lazily on first ask, because <c>PropManager.AdoptAuthoredProps</c> runs
    /// after every world node's <c>_Ready</c>.</summary>
    public IReadOnlyList<int> PropIds
    {
        get
        {
            Resolve();
            return _propIds;
        }
    }

    /// <summary>The rack's objects as nodes, for a capture script or a self-test that wants to
    /// look at where they are.</summary>
    public IReadOnlyList<NetworkedProp> Props
    {
        get
        {
            Resolve();
            return _props;
        }
    }

    public override void _Ready() => AddToGroup(Group);

    /// <summary><b>Is this one of mine?</b> −1 (nothing held) answers false, which is what lets
    /// every caller pass a held-prop id straight in without a null dance.</summary>
    public bool IsRackProp(int propId)
    {
        if (propId < 0)
            return false;
        Resolve();
        return _propIds.Contains(propId);
    }

    /// <summary>
    /// Walks this node's children once and remembers the three ids.
    ///
    /// <para><b>Lazy rather than in <see cref="_Ready"/>, and that is load-bearing.</b> An
    /// authored prop's id is assigned by <c>PropManager.AdoptAuthoredProps</c>, which runs on
    /// <c>Gameplay</c> AFTER the world node has been added to the tree — so at this node's
    /// <c>_Ready</c> every child's <c>PropId</c> is still its unassigned default. Resolving here
    /// would cache three wrong numbers, identically on every peer, and the symptom would be a
    /// START lamp that never lights.</para>
    /// </summary>
    private void Resolve()
    {
        if (_resolved)
            return;
        _propIds.Clear();
        _props.Clear();
        foreach (Node child in GetChildren())
        {
            if (child is not NetworkedProp prop || prop.PropId <= 0)
                continue;
            _propIds.Add(prop.PropId);
            _props.Add(prop);
        }
        if (_propIds.Count == 0)
            return;   // adoption has not run yet; ask again next frame.

        _resolved = true;
        if (_propIds.Count != ExpectedObjects)
        {
            // Not fatal — the round still works with two or four — but it is a level defect and
            // it changes the game, so it says so rather than being discovered in a playtest.
            GD.PushWarning($"[rack] {Name} carries {_propIds.Count} object(s); the design is "
                           + $"{ExpectedObjects} near-misses (ids "
                           + $"[{string.Join(", ", _propIds)}]).");
        }
    }
}
