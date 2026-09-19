namespace Sail.Game.Achievements;

/// <summary>
/// <b>"Bubblholic" (note 12.4), as a pure rule with no peer identity in its signature at all —
/// which is the multiplayer-correctness argument itself, not just a convenience.</b> Talon:
/// "collects all the bubbles ... I know this is a 'shared' multiplayer game, but this will be
/// fun nonetheless" — the bubble count is the one shared, server-authoritative tally
/// (<c>Sail.Game.Bubble.BubbleCounter</c>), never a per-player counter, and packet W7-5 is
/// explicit that this is the ruling, not a design gap to close by re-scoping it per player.
///
/// <para><paramref name="count"/> is meant to be <c>BubbleCounter.Count</c> — the server's
/// broadcast tally, applied identically on every peer via a <c>CallLocal</c> RPC
/// (<c>BubbleCounter.PopBroadcast</c>/<c>ResetBroadcast</c>) or a late-joiner's
/// <c>SyncState</c> — never a client's own locally-incremented guess.
/// <paramref name="total"/> is meant to be <c>BubbleCounter.BubbleCount</c>, the number of
/// bubbles this peer adopted while walking the world's authored scene
/// (<c>AdoptAuthoredBubbles</c>) — identical on every peer because every peer instantiates the
/// same scene file, so it needs no wire bit of its own (the same argument
/// <c>ToolStanceState</c>'s class doc makes for its own inputs). Feeding this rule those two
/// values is what makes the achievement fire from the authority: there is no third input this
/// rule could read a client's opinion through even if a caller wanted to.</para>
///
/// <para><c>total == 0</c> is guarded to <c>false</c> deliberately: a world with no adopted
/// bubbles (not yet synced, or a level with none) must never trivially "complete" the
/// achievement on its very first check.</para>
/// </summary>
public static class BubblholicRule
{
    public static bool AllPopped(int count, int total) => total > 0 && count >= total;
}
