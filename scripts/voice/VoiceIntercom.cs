using Godot;
using MpFoundation.Game.Round;

namespace MpFoundation.Voice;

/// <summary>
/// <b>The intercom's wiring</b> (VOICE-1): it binds <see cref="VoiceManager"/>'s three PA hooks
/// to the round's room map, and it is the only place in the game that assigns any of them.
///
/// <para><b>Talon's ruling, 2026-09-19, is what this file implements:</b> voice is always on and
/// always cross-room. Same room is proximity, different rooms is the PA/intercom — filtered, no
/// falloff, audible everywhere. Nothing mutes anyone, there is no push-to-talk change and there
/// is no mute-by-phase. Taunting and bluffing through a wall are the design, and the seeker
/// lying about how close they are is the mechanic the whole startle hangs on.</para>
///
/// <para><b>All three hooks come off ONE function.</b> <c>HideSeekDriver.RoomOf</c> answers
/// identically on the server and on every client (see its doc), so the client's per-listener
/// route and the relay's per-pair exemption cannot drift apart. The failure that discipline
/// prevents is specific and silent: <c>VoiceProximityGate</c>'s header says a gate that does not
/// know who is on PA silently mutes the intercom, and on this world the gate is ON by default
/// with the rooms 40 m apart against a 24 m cutoff — so a client-only wiring would leave every
/// cross-room voice culled at the relay and the intercom simply would not exist.</para>
///
/// <para><b>Only wired where there are rooms.</b> A world with no round geometry (the CI slab
/// worlds, <c>--world open</c>) gets no resolver at all rather than one that answers "everybody
/// is in the holding room" — the relay there is then byte-for-byte the build before this lane,
/// which is what keeps <c>Run-VoiceGateTest</c> measuring the gate rather than measuring
/// this.</para>
/// </summary>
public static class VoiceIntercom
{
    /// <summary>
    /// Binds the hooks. Idempotent, and safe to call before the peer has connected — every
    /// lambda resolves its peer ids when it is invoked, not now. <paramref name="context"/> is
    /// only read for its <c>Multiplayer</c> API.
    /// </summary>
    public static void Wire(Node context, HideSeekDriver driver, bool isServer)
    {
        VoiceManager voice = VoiceManager.Instance;
        voice.RoomResolver = driver.RoomOf;

        if (!isServer)
        {
            // THE CLIENT ROUTE. The listener is always the local player, so the per-talker
            // signature is complete here. Re-resolved every frame by VoiceManager._Process, which
            // is what makes a room change audible on the tick the round makes it rather than on
            // the next time somebody stops talking.
            //
            // GetUniqueId() is read INSIDE the lambda: this runs from Gameplay's setup, which is
            // before the client has connected, and a self id captured there would be the offline
            // peer's forever.
            voice.PaResolver = talker =>
                VoiceRouting.IsPa(driver.RoomOf(talker), driver.RoomOf((int)context.Multiplayer.GetUniqueId()));
            return;
        }

        // THE RELAY EXEMPTION, per (talker, listener) pair. This is the half that decides whether
        // the intercom exists at all on a world whose gate is on.
        voice.PaPairResolver = (talker, listener) =>
            VoiceRouting.IsPa(driver.RoomOf(talker), driver.RoomOf(listener));

        // The per-talker hook is still set on the server, for two reasons and neither is the
        // relay: it is what LogGateOnce reports as `paResolver=wired` (the one diagnostic that
        // says a PA-aware gate is in this build), and it is the fallback the relay uses if the
        // pairwise hook is ever unwired. "Is T on the PA" has no listener to be relative to here,
        // so it is answered as "is T on the PA for ANYONE" — true exactly when some other peer is
        // in a different room, which is never more restrictive than the pairwise rule and so can
        // never mute a pair the pairwise rule would have exempted.
        voice.PaResolver = talker =>
        {
            string room = driver.RoomOf(talker);
            foreach (int peer in context.Multiplayer.GetPeers())
                if (peer != talker && VoiceRouting.IsPa(room, driver.RoomOf(peer)))
                    return true;
            return false;
        };
    }
}
