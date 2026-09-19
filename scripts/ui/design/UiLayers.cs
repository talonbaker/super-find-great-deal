namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>The CanvasLayer ladder — every rung, in one place.</b>
///
/// <para>Before this file the ladder was seventeen magic numbers spread across eleven scripts
/// and three scenes, each with a comment naming the two or three neighbours its author happened
/// to know about. The audit found what that costs: two straight collisions (InteractPrompt and
/// the session summary both at 70; two world-furniture panels both at 75), and a pause
/// overlay sitting at layer 10 — <i>beneath</i> the HUD it is supposed to cover — which only
/// looked correct because it also raised <c>WorldUi.Suppressed</c>, a flag three of the overlays
/// above it never read. A stacking order that works by side effect is not an order.</para>
///
/// <para><b>How to add a rung:</b> pick the band your surface belongs to by what it IS, take the
/// next free number inside it, and add a member here. Never write a bare integer into a
/// CanvasLayer — a number chosen at the call site can only be checked against the neighbours
/// that author remembered.</para>
///
/// <para>The bands, lowest to highest, and the question each answers:</para>
/// <list type="number">
/// <item><b>60–79 world furniture and transient messaging</b> — anchored to something in the
/// world, or a line that passes through, and therefore under everything anchored to the
/// frame.</item>
/// <item><b>80–89 HUD</b> — the persistent readouts.</item>
/// <item><b>90–92 full-screen state</b> — a screen that owns the frame because the round is
/// between states.</item>
/// <item><b>93–95 sensory</b> — full-frame effects the world is doing TO the player (frost).
/// Above the HUD, because they are not information.</item>
/// <item><b>96–99 menus the player opened</b> — pause and everything reachable from it. Above
/// state and sensory alike: a player who presses pause has stopped playing, and nothing the
/// world is doing outranks that.</item>
/// <item><b>100–109 boot and loading</b> — covers the whole screen while up.</item>
/// <item><b>120+ diagnostics</b> — never part of the interface, always on top of it.</item>
/// </list>
/// </summary>
public static class UiLayers
{
    // --- 60–79: world furniture and transient messaging -----------------------------------------

    /// <summary>Transient one-line phase toasts. Left where it has always been rather than
    /// promoted above the HUD: the existing relative order is shipped and playtested, and this
    /// pass only moves the rungs the audit named as defects.</summary>
    public const int PhaseToast = 60;

    /// <summary>Transient one-line achievement-unlock toasts (W7-5). Its own rung rather than a
    /// reuse of <see cref="PhaseToast"/>'s number: they are two independent CanvasLayers, kept on
    /// separate files/classes so a concurrently-edited UI packet never shares a file with this
    /// one, and the ladder's own rule is "never write a bare integer" — a named constant next to
    /// phase toasts is how both stay checked by
    /// <c>UiLayerLadderTests.NoTwoSurfacesShareARung</c>.</summary>
    public const int AchievementToast = 61;

    /// <summary>The interact chip on whatever the player is looking at.</summary>
    public const int InteractPrompt = 70;

    /// <summary>The run-end summary. Was 70, colliding with <see cref="InteractPrompt"/>.</summary>
    public const int SessionSummary = 72;

    /// <summary>The plate warning what a destructive world control is about to cost, raised while
    /// the player stands in reach of it (LEVER-2, Talon's note 6). This band and not a higher one:
    /// it is transient messaging about a thing in the world, it takes no input, and a menu or state
    /// screen the player opened outranks it — which is also why it reads
    /// <c>WorldUi.Suppressed</c>. Its number sits above <see cref="SessionSummary"/> by arrival
    /// order only; the two can never be up at once, since a run-end summary means the lever is no
    /// longer reachable.</summary>
    public const int ConsequenceWarning = 73;

    // --- 80–89: the HUD -------------------------------------------------------------------------

    /// <summary>The persistent HUD — the bubble tally and the day/phase line.</summary>
    public const int GameHud = 80;

    /// <summary>The chill readout, beside the HUD it belongs to.</summary>
    public const int ChillReadout = 81;

    /// <summary>The worded quota strip: HUD furniture, under every state screen.</summary>
    public const int QuotaStrip = 82;

    // --- 90–92: full-screen state ---------------------------------------------------------------

    /// <summary>The nightfall treatment. One rung below the state screens so a verdict committed
    /// mid-treatment draws over it, never under it.</summary>
    public const int Nightfall = 90;

    /// <summary>Flow state screens: round intro, tally, upgrade lobby, loss, connecting gate.</summary>
    public const int StateScreen = 92;

    // --- 93–95: sensory ---------------------------------------------------------------------------

    /// <summary>The chill frost creeping in from the frame edge.</summary>
    public const int ChillCue = 94;

    // --- 96–99: menus the player opened -----------------------------------------------------------

    /// <summary>Pause. <b>Was 10</b> — beneath the HUD, the state screens and every sensory
    /// effect — and survived only because it happened to raise the world-UI suppression flag on
    /// the way in. It now simply covers what it is supposed to cover.</summary>
    public const int PauseOverlay = 96;

    /// <summary>Panels reachable from pause (how-to-play, the playtest foreword). Above pause,
    /// because pause is what opened them.</summary>
    public const int PauseChildPanel = 97;

    /// <summary>The development screenshot chrome.</summary>
    public const int DevScreenshot = 99;

    // --- 100+: boot, loading, diagnostics ------------------------------------------------------------

    /// <summary>The loading overlay — must cover the whole screen while it is up.</summary>
    public const int LoadingOverlay = 100;

    /// <summary>The performance readout. A developer instrument, not interface.</summary>
    public const int PerfHud = 120;

    /// <summary>Telemetry chrome, above all gameplay and menu UI.</summary>
    public const int Telemetry = 128;

    /// <summary>Every rung, for the test that proves no two surfaces share one.</summary>
    public static readonly (string Name, int Layer)[] All =
    {
        (nameof(AchievementToast), AchievementToast),
        (nameof(InteractPrompt), InteractPrompt),
        (nameof(SessionSummary), SessionSummary),
        (nameof(ConsequenceWarning), ConsequenceWarning),
        (nameof(GameHud), GameHud),
        (nameof(ChillReadout), ChillReadout),
        (nameof(QuotaStrip), QuotaStrip),
        (nameof(PhaseToast), PhaseToast),
        (nameof(Nightfall), Nightfall),
        (nameof(StateScreen), StateScreen),
        (nameof(ChillCue), ChillCue),
        (nameof(PauseOverlay), PauseOverlay),
        (nameof(PauseChildPanel), PauseChildPanel),
        (nameof(DevScreenshot), DevScreenshot),
        (nameof(LoadingOverlay), LoadingOverlay),
        (nameof(PerfHud), PerfHud),
        (nameof(Telemetry), Telemetry),
    };
}
