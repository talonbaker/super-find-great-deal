namespace MpFoundation.Game.Contracts;

/// <summary>
/// The night's spatial state: which night it is, and how far the light reaches.
///
/// <para>Canon (`CLAUDE.md`, direction reset 2026-08-08): escalation is spatial and it is the
/// end condition — the geography never changes, the lit area shrinks night over night, and you
/// can see how much game is left by looking at where the light stops.</para>
///
/// <para>R3 (the watcher) is a read-only consumer. It uses <see cref="LitRadiusM"/> for exactly
/// one thing: the boundary it may never cross. Lit ground is not somewhere the watcher is shy
/// of — it is ground it is forbidden, which is what gives the fire's radius a predator-defined
/// meaning rather than a lighting one (THRILL-BIBLE §12, the watcher entry).</para>
/// </summary>
public interface INightPressure
{
    int   NightIndex { get; }     // 1..5
    float LitRadiusM { get; }     // metres from the campfire
}
