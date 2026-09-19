using MpFoundation.Game.Sandbox;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// <b>Which HUD readouts this world actually has a game for.</b>
///
/// <para><b>The defect this closes.</b> <see cref="GameHud.Attach"/> was unconditional: every
/// world got every readout, whether or not it had a clock the player cares about or anything to
/// tally. Talon, 2026-08-27, on the Bubble Test level: <i>"I don't want to see quarters. I don't
/// want to see things like the watch in the upper right hand corner... I don't want to see the
/// mini map because that doesn't make sense anymore."</i> The level came up wearing the HUD of a
/// game that no longer exists.</para>
///
/// <para><b>Why this is not <c>WorldUi.Suppressed</c>.</b> That flag is all-or-nothing, it also
/// clears player nameplates, and it is owned by <c>FlowScreens</c> for menus — a world that
/// pinned it would be fighting the pause overlay for the same bit. What is needed here is a
/// per-widget decision, made once, at the moment the HUD is built.</para>
///
/// <para><b>Absence is the mechanism, not <c>Visible = false</c>.</b> A suppressed widget is
/// never constructed, so it costs no node and no <c>Tick()</c>. It also means a future reader
/// cannot mistake a hidden widget for a broken one.</para>
///
/// <para><b>Unknown world means the full HUD</b> — but read the next paragraph before relying on
/// that, because "unknown" almost never happens. <c>HudLayoutSelfTest</c> depends on that — it
/// measures the day/phase readout's real rect, and a default that removed it would turn a layout
/// test green by deleting what it measures.</para>
///
/// <para><b>TRAP: there is no such thing as "a lab scene with no world id".</b> This class's doc
/// used to claim a lab or self-test has no <c>NetworkManager</c> to ask. That is FALSE —
/// <c>NetworkManager</c> is an autoload, so <see cref="Current"/> never sees null; a scene
/// launched without <c>--world</c> reads <c>LaunchOptions.DefaultWorld</c>, which is
/// <b>"bubbletest"</b>, and gets the stripped profile. The layout and screen-flow suites both
/// pass an explicit non-bubbletest <c>--world</c> for precisely this reason and both record that
/// they are RED without it. If you are adding a per-world gate to something a lab, demo or capture
/// rig also builds, prefer passing the decision in as a parameter defaulted to the full behaviour
/// (see <c>FlowScreens.Attach</c>'s <c>roundScreens</c>) over reading <see cref="Current"/> where
/// no session exists. Full note: <c>LaunchOptions.DefaultWorld</c>.</para>
/// </summary>
public readonly struct HudProfile
{
    /// <summary>Top-centre: "DAY 2 · NIGHT".</summary>
    public bool DayPhase { get; private init; }

    /// <summary>Top-centre, under the day/phase line: the winter-cache strip
    /// (<c>QuotaStripWidget</c>, built by <c>FlowScreens.Attach</c> on its own layer).
    ///
    /// <para>Not in Talon's list, and included anyway because a headed capture found it reading
    /// "WINTER CACHE — The cache is empty." directly under the bubble tally. It is the same defect
    /// for the same reason — a readout of a system this world does not run — and it was invisible
    /// to the list because nobody had looked at the frame.</para>
    /// </summary>
    public bool QuotaStrip { get; private init; }

    /// <summary>Top-centre: the shared tally that used to be the bubble count. Opt-IN, unlike
    /// everything above. Nothing builds it since the fork pruned the bubbles (BASE-1,
    /// 2026-09-19) — the flag is kept so the slot has a name when HOLD-1 puts the score board in
    /// it, and every profile below sets it false.</summary>
    public bool BubbleCount { get; private init; }

    /// <summary>Top-centre: the round's phase, clock, your role and the round number
    /// (<see cref="RoundStripWidget"/>, ROUND-1). The one readout this game actually earns — every
    /// other flag on this struct answers "does this world run the system behind that number", and
    /// the supermarket runs exactly one such system.
    ///
    /// <para>On for any world with a round, off elsewhere. The widget holds itself off the frame
    /// until <c>HideSeekDriver</c> is synced regardless, so a world that leaves this on and never
    /// starts a round shows nothing rather than a plausible "HOLDING · ROUND 1".</para></summary>
    public bool RoundStrip { get; private init; }

    /// <summary>Bottom-right: the permanent "ESC · HOW TO PLAY" hint
    /// (<see cref="HudHowToPlayHint"/>). On everywhere by default, unlike every other flag here.
    ///
    /// <para><b>It is not a readout, so the rule above does not decide it.</b> Everything else on
    /// this struct answers "does this world run the system behind that number"; this one answers
    /// "can the player find the controls", and every world has controls. The bool exists because a
    /// world that owns its frame for its own reasons — a cinematic, a capture rig, some future
    /// world with a diegetic control surface — must be able to say no without editing
    /// <see cref="GameHud"/>.</para></summary>
    public bool HowToPlayHint { get; private init; }

    /// <summary>Every readout the game has. The default for any world that has not asked for
    /// something narrower.</summary>
    public static HudProfile Full => new()
    {
        DayPhase = true,
        QuotaStrip = true,
        BubbleCount = false,
        RoundStrip = false,
        HowToPlayHint = true,
    };

    /// <summary>
    /// The supermarket: no readout of a system this world does not run.
    ///
    /// <para>The rule, carried over verbatim from the level this profile replaced: a readout of a
    /// system that is not running is not neutral clutter, it is a promise the level does not keep.
    /// There is no day here (the cycle is inherited plumbing, not a deadline) and no quota, so
    /// both are off. ROUND-1's phase/timer strip and HOLD-1's board are the readouts this world
    /// will actually earn.</para>
    ///
    /// <para>The how-to-play hint survives that cut because it is not a readout — see
    /// <see cref="HowToPlayHint"/>.</para>
    /// </summary>
    public static HudProfile Supermarket => new()
    {
        DayPhase = false,
        QuotaStrip = false,
        BubbleCount = false,
        RoundStrip = true,
        HowToPlayHint = true,
    };

    /// <summary>The profile for a world id. One switch, so "which HUD does this world wear" has
    /// exactly one answer and adding a world is one arm. An unknown id falls back to
    /// <see cref="Full"/> (see the class doc for why the layout self-test depends on that).</summary>
    public static HudProfile For(string? worldId) => worldId switch
    {
        "supermarket" => Supermarket,
        _ => Full,
    };

    /// <summary>The profile for the world this process is actually running. The null-conditionals
    /// are defence only — <c>NetworkManager</c> is an autoload, so in practice this ALWAYS
    /// resolves a world id, and in a scene launched without <c>--world</c> that id is
    /// <c>LaunchOptions.DefaultWorld</c> ("supermarket"), NOT "no session". See the trap paragraph
    /// on this class before using it from anywhere a lab or self-test also reaches.</summary>
    public static HudProfile Current =>
        For(MpFoundation.NetworkManager.Instance?.Options?.World);
}
