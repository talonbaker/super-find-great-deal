namespace MpFoundation.Game.Contracts;

/// <summary>
/// How exposed a player is, right now, to something that hunts by sight.
///
/// <para>Shared across the 2026-08-09 MVP round-1 packets: R3 (the watcher) is the first
/// consumer, and the avatar-side stance/motion authority is the intended producer. Declared
/// as an interface rather than a concrete score so the watcher can be tested, and driven in a
/// lab, without an avatar, a network session or a stance machine existing.</para>
///
/// <para>The whole point of routing target selection through a single number is that the
/// "doe run" — a player who stands up and sprints pulling attention off their crouched
/// friends — costs nothing to build. It is this score read backwards, not a second system.</para>
/// </summary>
public interface IVisibilityScore
{
    /// 0.0 = effectively unseen, 1.0 = fully exposed. Contributors: stance
    /// (prone &lt; crouched &lt; standing), inside lit ground vs outside, moving vs still.
    float VisibilityFor(int peerId);
}
