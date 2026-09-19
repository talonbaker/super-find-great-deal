namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every number the round loop has</b> (SESSION-2; design doc
/// <c>docs/design/2026-09-15-round-loop.md</c> §2, built from D3 in
/// <c>REVIEW-2026-09-15-BOTTLE-FREEWAY-STATE.md</c> §4).
///
/// <para><b>A plain data record, exactly <see cref="MpFoundation.Dev.Session.ShiftLoopTuning"/>'s
/// idiom.</b> This struct holds the numbers only; it does not clamp itself. The floor is applied
/// at the point each phase's timer is ARMED — <see cref="RoundLoop"/>'s <c>EnterX</c> helpers,
/// <c>Math.Max(t.XSec, MinTimerSec)</c> — which is exactly where <c>ShiftLoop.EnterShift</c> /
/// <c>EnterLaunching</c> / <c>EnterReturning</c> apply theirs. A struct that clamped itself on
/// construction would still need the same floor re-applied at every <c>with</c> expression a test
/// or a tuning panel writes, so the floor belongs at the one place it is actually read.</para>
///
/// <para><b>The floor is 1 s, not the lab's looser 0 / 0.001 f.</b> The session loop's own design
/// doc (<c>docs/design/2026-09-07-session-loop-spec.md</c> §2.1) states every timer "clamps to
/// >= 1 s and a non-positive configured value clamps and logs rather than zero-firing" — sourced
/// from <c>PlaythroughMachine.MinTimerSec</c> (<c>PlaythroughMachine.cs:54</c>) and its
/// <c>ClampTimer</c> shape, which <c>ShiftLoop</c>'s own class doc says was "copied verbatim so
/// the lab and the shipped machine cannot disagree about what a bad knob does." The literal
/// <c>ShiftLoop.cs</c> transition helpers use <c>Math.Max(t.X, 0f)</c> / <c>0.001f</c> instead —
/// a looser floor than their own design doc states. This packet's brief is explicit ("clamped
/// >= 1 s exactly as ShiftLoop clamps"), so <c>RoundLoop</c> follows the WRITTEN contract
/// (1 s, matching <c>PlaythroughMachine.MinTimerSec</c>) rather than the narrower literal floor
/// found in <c>ShiftLoop.cs</c>'s current code. See the design doc's §2.1 for this call, stated
/// rather than silently reconciled.</para>
/// </summary>
public readonly record struct RoundLoopTuning
{
    /// <summary>Every phase timer floors here. Matches
    /// <see cref="Sail.Game.Run.PlaythroughMachine.MinTimerSec"/>.</summary>
    public const float MinTimerSec = 1f;

    /// <summary>How long Gathering waits once the first human is present, before Countdown
    /// starts anyway. Lab default 20 s (D3).</summary>
    public float GatherSec { get; init; }

    /// <summary>The shared-start hold. Lab default 5 s (D3).</summary>
    public float CountdownSec { get; init; }

    /// <summary>One round's length. Lab default 180 s (D3).</summary>
    public float RoundSec { get; init; }

    /// <summary>How long the tally card holds before the next Countdown. Lab default 8 s (D3).</summary>
    public float TallySec { get; init; }

    /// <summary>D3's numbers, unchanged.</summary>
    public static readonly RoundLoopTuning Default = new()
    {
        GatherSec = 20f,
        CountdownSec = 5f,
        RoundSec = 180f,
        TallySec = 8f,
    };

    /// <summary>The live tuning, <c>MopedTuning.Current</c> / <c>ShiftLoopTuning.Current</c>'s
    /// one-mutable-static idiom, so the loop, a driver and a panel can never read two different
    /// sessions.</summary>
    public static RoundLoopTuning Current { get; set; } = Default;
}
