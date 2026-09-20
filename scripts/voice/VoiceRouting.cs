using System;
using System.Collections.Generic;

namespace MpFoundation.Voice;

/// <summary>
/// <b>The intercom's decision table</b> (VOICE-1): given the talker's room and the listener's
/// room, which route does that voice take on that listener's machine?
///
/// <para><b>Listener-relative, which is the whole shape of the feature.</b> "Is T on the PA" has
/// no answer on its own — T is on the PA for everyone in a different room and on proximity for
/// everyone in the same one. On a client the listener is always the local player, which is why
/// <c>VoiceManager.PaResolver</c>'s per-talker signature works there; on the SERVER the relay has
/// to answer it once per (talker, listener) PAIR, which is what
/// <c>VoiceManager.PaPairResolver</c> exists for. Both call this.</para>
///
/// <para><b>Unknown fails open to PA, never to silence.</b> An empty room string means the round
/// cannot say where somebody is (<see cref="Game.Round.RoundRooms.Unknown"/>) — mid-Tally, a peer
/// the round has never placed, a client that has not synced yet. The PA route is audible
/// everywhere with no distance falloff, so resolving an unknown to PA costs one filtered voice
/// somebody did not strictly need to hear; resolving it to proximity would hand the server's
/// distance cull a 40 m pair and mute a real player with no log line to show for it. Same
/// direction <c>VoiceRelayDecider</c> already fails in for an unresolvable position, and the same
/// reason: the report a player writes is "I could not hear anyone".</para>
///
/// <para><b>Engine-free.</b> No Godot types, so the whole table is held by <c>tests/unit</c>.</para>
/// </summary>
public static class VoiceRouting
{
    /// <summary>The route names, as the routing verdict and the bot harness print them. Lower
    /// case and stable: <c>BotHarness</c> and every suite string-match on these.</summary>
    public const string RoutePa = "pa";

    /// <inheritdoc cref="RoutePa"/>
    public const string RouteProximity = "proximity";

    /// <summary>Talker and listener are in different rooms — or at least one of the two rooms is
    /// unknown. See the class doc for why unknown lands here rather than on proximity.</summary>
    public static bool IsPa(string? talkerRoom, string? listenerRoom)
    {
        if (string.IsNullOrEmpty(talkerRoom) || string.IsNullOrEmpty(listenerRoom))
            return true;
        return !string.Equals(talkerRoom, listenerRoom, StringComparison.Ordinal);
    }

    /// <inheritdoc cref="IsPa"/>
    public static string RouteFor(string? talkerRoom, string? listenerRoom) =>
        IsPa(talkerRoom, listenerRoom) ? RoutePa : RouteProximity;

    /// <summary>
    /// <b>The intercom wetness trim</b>, as a multiplier on the PA reverb's shipped wet mix
    /// (<see cref="VoiceConfig.PaReverbWet"/>).
    ///
    /// <para><b>0 dB is exactly what ships</b>, which is the property that makes this a trim
    /// rather than a retune: <c>WetFromDb(0)</c> returns <see cref="VoiceConfig.PaReverbWet"/>
    /// unchanged, so adding the knob cannot have changed the sound. Negative pulls the room off
    /// the voice (-6 dB is half the wet mix, -60 dB is dry); positive is clamped at fully wet,
    /// because Godot's <c>AudioEffectReverb.Wet</c> is a 0..1 mix and a value past 1 is not a
    /// louder room, it is an invalid parameter.</para>
    ///
    /// <para>The knob is wet rather than volume because the note it answers is "too muddy for
    /// taunting" — mud is the reverb tail smearing consonants, and turning the PA down makes a
    /// muddy voice a quiet muddy voice.</para>
    /// </summary>
    public static float WetFromDb(float db)
    {
        if (float.IsNaN(db))
            return VoiceConfig.PaReverbWet;
        float wet = VoiceConfig.PaReverbWet * MathF.Pow(10f, db / 20f);
        return Math.Clamp(wet, 0f, 1f);
    }

    /// <summary>One listener's view of one talker, as the routing verdict carries it.</summary>
    public sealed record PeerVerdict(int Id, string Name, string Room, string Route);

    /// <summary>
    /// <b>The routing verdict</b> — what THIS process has decided about every voice it can hear,
    /// as one serialisable object. The suites assert on it rather than on a log sentence, and the
    /// bot harness embeds it in every sample (<c>vroute</c>).
    ///
    /// <para><b>It carries the ROOMS, not only the routes</b>, and that is what stops a cell
    /// passing for the wrong reason: "both peers say pa" is also what two peers whose rooms are
    /// both unknown would say, and that is the fail-open path, not the intercom working.</para>
    /// </summary>
    public sealed record Verdict(
        int Self,
        string Room,
        bool Server,
        bool GateOn,
        bool PaResolverWired,
        bool PaPairResolverWired,
        bool RoomResolverWired,
        List<PeerVerdict> Peers);
}
