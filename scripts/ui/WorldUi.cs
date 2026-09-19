namespace MpFoundation.Ui;

/// <summary>
/// One switch for every piece of world-anchored UI — anything drawn at a projected
/// world position rather than in screen space: the interact chip, player nameplates,
/// and whatever else grows here.
///
/// This exists as a shared flag rather than a per-widget one on purpose. The pause
/// overlay used to suppress the interact chip directly, which meant each new piece of
/// world UI had to remember to opt in, and the nameplate never did — it kept drawing
/// over the pause menu. That is MECHANICS-BIBLE 2: a state change (pause) that updated
/// one dependent system and silently missed a sibling. A single flag every consumer
/// reads makes the next widget correct by default instead of correct by memory.
/// </summary>
public static class WorldUi
{
    /// <summary>While true, world-anchored UI hides regardless of targets or distance.
    /// Set by menus (the pause overlay) that should never have world furniture drawn
    /// over them.</summary>
    public static bool Suppressed { get; set; }
}
