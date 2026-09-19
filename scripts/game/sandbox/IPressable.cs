using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>A fixed world control an avatar walks up to and presses once</b> — a lever, a switch, a
/// button. Distinguished from everything else <c>InteractHighlighter</c> polls by what it is NOT:
/// it is not carried (Carryable), it does not move the player, it does not open a panel, and it
/// is not a bespoke server funnel wired by hand in <c>Gameplay</c> (the original level had two).
///
/// <para><b>Why a general interface rather than a seventh named candidate.</b> The highlighter
/// already knows six concrete types by name, and each addition has meant an edit in three files.
/// The seventh — BT-8's bubble reset lever — sits in <c>Sail.Game.Bubble</c>, and naming it in
/// <c>MpFoundation.Game.Sandbox</c> would point the shared interaction core at one level's
/// feature. So this is the one arm that does not name a type: join <see cref="Group"/>, declare a
/// radius, implement <see cref="Press"/>, and the prompt, the shimmer and the key press all
/// work.</para>
///
/// <para><b><see cref="Press"/> runs on the pressing client</b>, exactly like
/// <c>WallMapPanel.Toggle</c> and <c>BedSleepFade.RequestSleep</c> before it. An implementation
/// whose effect is shared state therefore ASKS the server (an RPC) rather than acting — the
/// client only ever decides whether to ask, and the server re-checks whatever it cares about.
/// That is the same rule <c>SandboxAvatar.WorldInteract</c>'s own doc states, and it is why this
/// interface returns nothing: there is no local result to report.</para>
/// </summary>
public interface IPressable : IHighlightable
{
    /// <summary>The scene-tree group every pressable joins, so the highlighter finds them without
    /// the core knowing a single implementing type.</summary>
    const string Group = "pressable";

    /// <summary>How close the avatar has to be. Per-instance rather than a shared constant
    /// because a lever on a plinth and a button on a wall are not the same reach, and
    /// <c>InteractTargeting.Pick</c> already takes a range per candidate.</summary>
    float PressRadiusM { get; }

    /// <summary>The player pressed Interact with this as the winning candidate. Client-side; see
    /// the interface doc.</summary>
    void Press();
}
