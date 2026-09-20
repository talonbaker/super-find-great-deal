namespace MpFoundation.Game.Props;

/// <summary>
/// <b>What a refused carry verb says to the player who pressed the key</b> — one sentence per
/// denial ordinal, for both <see cref="PropManager.GrabDenial"/> and
/// <see cref="PropManager.PlaceDenial"/>.
///
/// <para><b>Why the words live here rather than in the avatar or in the RPC.</b> The ordinal is
/// the wire contract and must stay append-only; the sentence is presentation and changes whenever
/// a playtest says it reads wrong. Keeping them in one plain static class next to the enums means
/// the pair can be checked mechanically — <c>CarryRefusalTextTests</c> asserts that EVERY ordinal
/// but <c>None</c> has its own sentence, so adding a refusal reason and forgetting to say it out
/// loud turns the xUnit suite red rather than shipping a bump with no explanation. That check is
/// the point of the file: this repo's one shipped in-world control failed a playtest because its
/// warning <i>"never said which key"</i> (INTERACTION-BIBLE §3, §7), and "silently ignored" is a
/// defect class (§2), not an implementation choice.</para>
///
/// <para>Engine-free on purpose: a plain static class rather than methods on
/// <c>SandboxAvatar</c>, so the check above runs under plain xUnit without a scene tree.</para>
/// </summary>
public static class CarryRefusalText
{
    /// <summary>The heading over a refused pick-up.</summary>
    public const string GrabHeading = "CAN'T PICK THAT UP";

    /// <summary>The heading over a refused place.</summary>
    public const string PlaceHeading = "CAN'T PUT IT THERE";

    /// <summary>The last-resort sentence. Reaching it means an ordinal arrived that this build
    /// does not know — an older client talking to a newer server — and even then the player is
    /// told SOMETHING rather than left with a silent bump.</summary>
    public const string Unknown = "That didn't work.";

    /// <summary>Why a pick-up was refused, in the player's terms.</summary>
    public static string For(PropManager.GrabDenial reason) => reason switch
    {
        PropManager.GrabDenial.HandsFull => "Your hands are full — put that down first.",
        PropManager.GrabDenial.OutOfRange => "Too far away — get closer.",
        PropManager.GrabDenial.Taken => "Somebody else got there first.",
        PropManager.GrabDenial.AlreadyHeld => "You're already holding it.",
        PropManager.GrabDenial.Gone => "That's not there any more.",
        _ => Unknown,
    };

    /// <summary>Why a place was refused, in the player's terms.</summary>
    public static string For(PropManager.PlaceDenial reason) => reason switch
    {
        PropManager.PlaceDenial.NotHolding => "You're not holding that.",
        PropManager.PlaceDenial.OutOfRange => "Too far away — get closer.",
        PropManager.PlaceDenial.TooFarToPlace => "That's out of arm's reach.",
        PropManager.PlaceDenial.DoesNotFitThere => "It doesn't fit there.",
        PropManager.PlaceDenial.OutsideRoom => "That's outside the room.",
        PropManager.PlaceDenial.NotAllowedHere => "That doesn't go there.",
        PropManager.PlaceDenial.Gone => "That's not there any more.",
        _ => Unknown,
    };
}
