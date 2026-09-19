namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every number the hide-seek loop has</b> (packet ROUND-1 §1; program
/// <c>docs/design/2026-09-19-supermarket-mvp.md</c> §2).
///
/// <para><b>A plain data record; it does not clamp itself.</b> Inherited verbatim from the loop
/// this replaces, and the reasoning is unchanged: the floor is applied where each phase's timer is
/// ARMED — the <c>EnterX</c> helpers in <see cref="HideSeekLoop"/>,
/// <c>Math.Max(t.XSec, MinTimerSec)</c> — because a struct that clamped on construction would
/// still need the same floor re-applied at every <c>with</c> expression a test or a tuning panel
/// writes. The floor belongs at the one place it is actually read.</para>
///
/// <para><b>The floor is 1 s.</b> Carried across from <c>RoundLoopTuning.MinTimerSec</c>, which
/// took it from the session loop's written contract ("every timer clamps to &gt;= 1 s and a
/// non-positive configured value clamps and logs rather than zero-firing"). A zero-length phase is
/// a phase whose transition and whose entry teleport land on the same tick, which is exactly the
/// shape that makes a room change unobservable.</para>
/// </summary>
public readonly record struct HideSeekTuning
{
    /// <summary>Every phase timer floors here.</summary>
    public const float MinTimerSec = 1f;

    /// <summary>How long the hider gets in the search room. Packet default 30 s.</summary>
    public float HidingSec { get; init; }

    /// <summary>
    /// <b>The one extension, and it is not generosity.</b> If the Hiding buzzer finds the target
    /// still in the hider's hands or failing REACH-1's reachability check, the loop does NOT start
    /// Seeking on a broken hide — a seeker sent to find something inside a wall is the worst
    /// outcome this design has (program §5b). Hiding is extended by this much, ONCE, and the
    /// reason is broadcast so the hider is told what to fix rather than left wondering why the
    /// clock moved. Still broken after the grace and the round goes to Tally with the hider
    /// scoring nothing: the hide failed, and the seeker cannot be asked to find it.
    /// </summary>
    public float HidingGraceSec { get; init; }

    /// <summary>The seek cap. Packet default 180 s.</summary>
    public float SeekingSec { get; init; }

    /// <summary>How long the card holds before the reset edge. Packet default 6 s.</summary>
    public float TallySec { get; init; }

    /// <summary>The packet's numbers, unchanged.</summary>
    public static readonly HideSeekTuning Default = new()
    {
        HidingSec = 30f,
        HidingGraceSec = 10f,
        SeekingSec = 180f,
        TallySec = 6f,
    };

    /// <summary>The live tuning — the one-mutable-static idiom the rest of this codebase's tuning
    /// records use, so the loop, the driver and a future tuning panel can never read two different
    /// sessions.</summary>
    public static HideSeekTuning Current { get; set; } = Default;
}
