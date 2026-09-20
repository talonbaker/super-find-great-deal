namespace MpFoundation.Net;

/// <summary>
/// Where a terminal client-side session failure sends the player, and what it tells them
/// when it gets there. One pure function, so the decision can be asserted as a table without
/// an engine, a Steam client or a live relay.
///
/// <para><b>Why this is not just <c>ScenePaths.JoinMenu</c> any more</b> (F1 of the
/// 2026-08-30 master review). Every terminal failure used to land on the Join screen. For a
/// joiner that is right — it is the screen they came from and the one they retry on. For a
/// HOST it was wrong in three ways at once: they were dumped on a screen they never chose;
/// the room code they had just read aloud to a friend was already dead (the failure path
/// destroys the child server and, as sole member, the Steam lobby the code resolves
/// through); and nothing on the Join screen tells them hosting is over or offers to start
/// it again. On the exact path the cross-platform playtest is defined by, that reads as the
/// session falling apart.</para>
///
/// <para><b>The dead lobby is deliberate, not collateral.</b> A joinable lobby with no server
/// behind it is worse than none — the friend gets a hang instead of a refusal. Keeping the
/// teardown and fixing the host's landing is the trade: the joiner sees a clean
/// <c>Room "XXXXXX" not found.</c> while the host is already back on the Host screen, told
/// what happened, one button from a new code.</para>
/// </summary>
public static class SessionFailureRoute
{
    /// <summary>What a host-flow failure is prefixed with. Public so a test can assert the
    /// message the player actually reads rather than re-deriving it.</summary>
    public const string HostingEndedPrefix = "Hosting ended: ";

    /// <summary>Appended to a host-flow failure. The room code stops resolving the moment its
    /// lobby dies, so telling the host their old code is gone is not a nicety — without it
    /// they will keep reading the dead code out.</summary>
    public const string RehostHint = "Your old room code no longer works — start hosting again for a new one.";

    /// <summary>Used when a failure arrives with nothing to say. Never leave the player on a
    /// screen with an empty error label: "it just went back" is indistinguishable from a bug.</summary>
    public const string UnknownReason = "The connection failed.";

    /// <summary>
    /// Resolves a terminal failure into the scene to load and the line to show there.
    /// </summary>
    /// <param name="hostFlow">
    /// <see cref="NetworkManager.IsHostFlow"/> — read BEFORE
    /// <see cref="NetworkManager.ResetToOffline"/>, which clears it.
    /// </param>
    /// <param name="reason">The typed reason the failure already carries (a handshake error,
    /// a timeout, a room-lookup failure). Passed through verbatim for a joiner.</param>
    public static (string Scene, string Message) For(bool hostFlow, string? reason)
    {
        string clean = (reason ?? "").Trim();
        if (clean.Length == 0)
            clean = UnknownReason;

        if (!hostFlow)
            return (ScenePaths.JoinMenu, clean);

        return (ScenePaths.HostMenu, HostingEndedPrefix + Sentence(clean) + " " + RehostHint);
    }

    /// <summary>The reasons are a mix of sentences ("Room \"ABCD\" not found.") and fragments
    /// ("Connection timed out"); a host-flow message concatenates one with the re-host hint,
    /// so the join has to punctuate or the two run together into a single unreadable line.</summary>
    private static string Sentence(string text)
    {
        char last = text[text.Length - 1];
        return last is '.' or '!' or '?' ? text : text + ".";
    }
}
