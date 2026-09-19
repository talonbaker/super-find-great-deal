using Godot;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>Test-only, server-only: complete the level on a schedule.</b> CELEBRATE-1's harness
/// (<c>--bubble-pop-all-at</c>), and the one owner of that behaviour so the BT-6 fixture and the
/// real level cannot drift apart about what "pop everything" means.
///
/// <para><b>Through the real adjudication path, not a back door.</b> Each mark calls
/// <see cref="BubbleCounter.ServerPopAllForTest"/>, which loops the counter's own
/// <c>ServerPop</c> — the method <c>Bubble.OnServerBodyEntered</c> calls when a player's collider
/// touches a sphere. One bit per bubble, one broadcast per bubble, and the last one is what makes
/// the completion announcement fire. Reaching into <c>BubbleCounterState</c> instead would test
/// nothing that ships.</para>
///
/// <para><b>This class is a CLOCK and nothing else</b>, deliberately: the pop loop lives on the
/// counter because <c>BubbleCounterStateTests.NoClientPathCanPopABubble</c> sweeps every file in
/// <c>scripts/**</c> for a <c>ServerPop</c> caller outside the bubble entity and fails on one.
/// That audit is the reason a client cannot drive a pop, and a test harness is not a good enough
/// reason to widen it. See <c>BubbleCounter.ServerPopAllForTest</c>'s own doc.</para>
///
/// <para><b>Why a schedule rather than a walk.</b> Completion is the thing under test, and no bot
/// route reaches a hundred authored bubbles scattered across seven sections deterministically —
/// nor six fixture bubbles in two corridors, which are placed eight metres apart precisely so a
/// straight-line walk pops exactly two of them. A route that reliably collected them all would be
/// a second, far more fragile determinism argument bought for no gain: everything actually under
/// test here is downstream of the last <c>ServerPop</c>.</para>
///
/// <para><b>Inert on every peer that is not the server</b>, and on every launch without the flag —
/// <c>Gameplay</c> only builds one when <c>--bubble-pop-all-at</c> is given. It writes nothing
/// else, unlocks nothing, and its marks are absolute seconds on its own clock, which starts when
/// it enters the tree.</para>
/// </summary>
public partial class BubbleCompletionSchedule : Node
{
    public const string NodeName = "BubbleCompletionSchedule";

    /// <summary>The log line the harness greps for each fired mark. Named so a runner matches a
    /// constant rather than a retyped string.</summary>
    public const string LogPrefix = "[bubbletest] pop-all";

    /// <summary>Server or client. Set before this node enters the tree; a client's copy does
    /// nothing at all.</summary>
    public bool IsServer { get; set; }

    /// <summary>Seconds into this node's life at which every remaining bubble is popped. Negative
    /// = never.</summary>
    public double PopAllAtSec { get; set; } = -1;

    /// <summary>An optional SECOND full pop, for the reset-then-recomplete cycle. Negative =
    /// never. Derived by the caller from whatever put the bubbles back, never typed beside
    /// it.</summary>
    public double RecompleteAtSec { get; set; } = -1;

    private double _clock;
    private bool _firstFired;
    private bool _secondFired;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (!IsServer)
            return;
        // Ordered first-then-second and each latched, so a run whose two marks were mis-scheduled
        // to collide resolves the same way on every machine rather than differently
        // (MECHANICS-BIBLE §3). A harness asserts the marks are ordered; this is the fixed order
        // it asserts about.
        if (!_firstFired && PopAllAtSec >= 0 && _clock >= PopAllAtSec)
        {
            _firstFired = true;
            PopEveryBubble("first completion");
        }
        if (_firstFired && !_secondFired && RecompleteAtSec >= 0 && _clock >= RecompleteAtSec)
        {
            _secondFired = true;
            PopEveryBubble("recompletion after reset");
        }
    }

    private void PopEveryBubble(string why)
    {
        if (BubbleCounter.Instance is not { } counter)
        {
            GD.PushWarning($"{LogPrefix} ({why}): no BubbleCounter in this world; nothing to pop.");
            return;
        }
        int before = counter.Count;
        int total = counter.BubbleCount;
        counter.ServerPopAllForTest();
        GD.Print($"{LogPrefix} ({why}): tally {before} -> {counter.Count} of {total} at {_clock:F3}s");
    }
}
