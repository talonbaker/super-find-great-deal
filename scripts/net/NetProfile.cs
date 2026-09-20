namespace MpFoundation.Net;

/// <summary>
/// The single per-title configuration surface for the networking foundation.
///
/// This is the file you edit when you reuse this stack for a new game. Everything here is a
/// transport / session / replication knob that a *different* game would legitimately set
/// differently — the App ID and lobby tag that scope matchmaking, the player/connection caps
/// every budget is calibrated to, the simulation tick and snapshot cadence, the reliable RPC
/// channel assignments, and the codec bounds. Gameplay *feel* (movement speed, gravity, jump,
/// cosmetic thresholds) deliberately lives with the avatar/motor that owns it — that is the
/// game, not the foundation — so it is NOT here.
///
/// Everything is a compile-time <c>const</c> on purpose: the channel values are consumed inside
/// <c>[Rpc(TransferChannel = ...)]</c> attributes, which only accept constant expressions. The
/// legacy constant names scattered across the codebase (<c>Protocol.Version</c>,
/// <c>NetCodec.PropChannel</c>, <c>AvatarMotor.TickRate</c>, …) now alias back to these so a
/// port touches one file, not twenty, while existing call sites keep compiling unchanged.
/// </summary>
public static class NetProfile
{
    // --- Protocol identity -------------------------------------------------------
    /// <summary>Wire/replication version. Bump on ANY change to the handshake or the replicated
    /// data shape so mismatched builds refuse to connect instead of misbehaving. History:
    /// v2 added proximity voice; v3 moved to server-authoritative movement (input+snapshot);
    /// v4 added the optional room-code field to the client handshake (capability join gate);
    /// v5 grew the input entry (aim-raise flag + aim yaw/pitch) and the snapshot (aim stance +
    /// steady-elapsed) for the WP-L3 shared aim substrate; v6 added a since-removed camera verb's
    /// shutter edge into the input entry's existing buttons byte for the L4
    /// verb — no byte-count change (a spare bit), but the byte's MEANING changed, which
    /// is exactly the "any change to the replicated data shape" case this field exists to catch;
    /// v7 added that camera's flash broadcast, a brand-new RPC method for the L5 flash beacon —
    /// a build still on v6 has no such method to dispatch to, the same "mismatched builds must
    /// refuse to connect, not misbehave" case v6 itself was bumped for;
    /// v8 (two-slot carry, 2026-08-07) spends the buttons byte's last two bits on the slot-select
    /// edge + index (NetCodec.FlagSelectSlot / FlagSelectSlotIndex — same no-byte-count-change,
    /// meaning-changed case as v6) and, in the same bump, new RPC methods and new PropKind
    /// ordinals for systems that have since been cut from this build. A PropKind ordinal is a
    /// data-shape change of the worst kind across builds: a stale client receiving an unknown kind
    /// falls through <c>NetworkedProp.Init</c>'s switch to the Crate default and renders the wrong
    /// object instead of failing loudly, which is exactly what this field exists to convert into a
    /// refused connection.</summary>
    /// Server and client are the same binary here, so "both updated" is structural rather than a
    /// coordination step; the handshake gate is what makes a stale peer refuse rather than
    /// misbehave.
    ///
    /// <para><b>v9 (2026-08-08, W2 lake-water contract).</b> The snapshot's flags byte (span[5])
    /// gained three previously-always-zero meanings: a two-bit water state, Soaked, and
    /// ControlLocked. The packet LENGTH is unchanged at 51 bytes, which is exactly why the bump
    /// is mandatory rather than optional — a v8 peer would parse a v9 snapshot without any error
    /// at all and simply never see a teammate swim, go under, or lose control. Silently-wrong is
    /// what this field exists to convert into a refused connection. <c>WaterService</c> also adds
    /// two RPCs, which a v8 build has no methods to dispatch to.</para>
    ///
    /// <para><b>v10 (2026-08-09, phase 1c failure states).</b> The snapshot flags byte's last
    /// three bits, <b>spent together in one deliberate pass</b>: a two-bit incapacitation state
    /// (bits 5–6, one of its four values reserved for a future third state) and the momentary
    /// impulse-ragdoll bit (bit 7). Packet length is unchanged at 51 bytes — again exactly why the
    /// bump is mandatory: a v9 peer would parse a v10 snapshot cleanly and simply never see a
    /// teammate knocked out or frozen solid, which is the silently-wrong failure this field
    /// exists to refuse. <c>IncapacitationService</c> also adds RPCs on the new
    /// <see cref="IncapacityChannel"/>, which a v9 build has no methods to dispatch to.</para>
    ///
    /// <para><b>The snapshot flags byte is now FULL</b> (see <c>NetCodec</c>'s own note at
    /// <c>FlagImpulseRagdoll</c>). The next snapshot flag is a layout change and a length change,
    /// not another spare-bit reuse. Said here as well as at the bit, because this is the field
    /// people read when they are about to add one.</para></summary>
    /// <para><b>v10 (Story #180)</b> was also spent, in the same bump, on RPC-shape changes and two
    /// more PropKind ordinals for systems that have since been cut from this build.</para>
    ///
    /// <para><b>v11 (2026-08-16, SKID-1 — the turnaround skid).</b> The first snapshot change in
    /// this list that costs LENGTH: <c>MoveState.SkidRemaining</c> is a float appended at span[51],
    /// so the packet goes 51 -> 55 bytes (<c>NetCodec.SnapshotBytes</c>). Every prior addition rode
    /// a spare bit; both flags bytes have been documented full since v10, and the skid needs a
    /// duration rather than a flag. The bump would be mandatory even without the length change, for
    /// the v9/v10 reason: a v10 peer parsing this would see a teammate turn on the spot while their
    /// own client slid them, which is a divergence rather than a cosmetic gap. As it happens the
    /// length change makes a stale peer's <c>UnpackSnapshot</c> return null outright, so this one
    /// fails loudly on both sides of the gate.</para>
    ///
    /// <para><b>v12 (2026-08-26, MOVE-3 — variable jump height).</b> The second <i>input</i>-side
    /// length change in this list: <c>MoveIntent.JumpHeld</c> needs a bit and the buttons byte was
    /// documented full at v8, so the input entry gains a second buttons byte and goes 25 -> 26
    /// bytes (<c>NetCodec.InputEntryBytes</c>). The snapshot is untouched at 55 bytes — the jump
    /// cut is a gravity term derived from the intent, deliberately, so no <c>MoveState</c> field
    /// and no snapshot bit were spent. The bump would be mandatory on meaning alone: a v11 peer
    /// would simulate every remote jump at full height while its owner cut it, which is a
    /// divergence rather than a cosmetic gap. As it happens the length change makes it fail loudly
    /// too — <c>UnpackInputs</c>'s length check returns null outright on a mismatched entry
    /// size.</para>
    ///
    /// <para><b>v13 (2026-08-27, MOVE-5 — the movement verb state machine).</b> The second
    /// <i>snapshot</i> length change in this list, for the reason the flags byte's own comment
    /// predicted at v10: the byte at <c>span[5]</c> has been documented full since then, so the
    /// verb, the chain depth and the air-jump counter need a second snapshot flags byte
    /// (<c>flags2</c> at <c>span[55]</c>: 2 + 3 + 2 bits, one spare) and the two tick clocks need a
    /// byte each. 55 -> 58 bytes (<c>NetCodec.SnapshotBytes</c>). <b>The input entry is untouched at
    /// 26</b> — every verb in the wave is carried by <c>MoveIntent.Jump</c>, <c>JumpHeld</c> and
    /// <c>MoveDir</c>, all three already on the wire, so <c>buttons2</c> keeps its seven free bits.
    /// The bump would be mandatory on meaning alone: a v12 peer would render a teammate's 5.9 m
    /// carve as an upright run and would re-grant a spent air jump on every replayed tick, which is
    /// a divergence rather than a cosmetic gap. As it happens the length change makes it fail
    /// loudly too — <c>UnpackSnapshot</c>'s length check returns null outright.</para>
    ///
    /// <para><b>Bandwidth cost, priced rather than assumed.</b> At
    /// <see cref="SnapshotIntervalTicks"/> = 2 (30 Hz): 90 B/s per replicated avatar stream, and in
    /// a 6-player session <c>6 avatars x 30 Hz x 3 B x 5 recipients</c> = <b>2.64 kB/s</b> added to
    /// the server's egress against a 48.3 kB/s baseline for the same traffic; 0.53 kB/s per client
    /// ingress.</para></summary>
    ///
    /// <para><b>v15 (2026-09-19, BASE-1 — the fork into Super Find Great Deal).</b> No message
    /// shape changed in this bump and none is expected to: it is spent entirely on making a Watis
    /// World build and a Super Find Great Deal build refuse each other at the handshake. The two
    /// games share an App ID (Spacewar, <see cref="FallbackSteamAppId"/>), share a transport, and
    /// were the same binary yesterday, so without this a Watis client would connect to this server
    /// and be handed a world, an avatar and a prop stream it has no matching content for — the
    /// silently-wrong failure this field exists to convert into a refused connection. The lobby
    /// scope tag below is the matchmaking half of the same fence; this is the ENet/direct-connect
    /// half, which no lobby tag can reach.</para>
    ///
    /// <para><b>v16 (2026-09-19, SFX-1 — the supermarket's product shapes).</b> Three appended
    /// <c>PropKind</c> ordinals: <c>Can = 2</c>, <c>Box = 3</c>, <c>Produce = 4</c>. No message
    /// shape changed and no byte count moved — the kind was already an int in the spawner's data
    /// array and in <c>PropState</c>. The bump is mandatory anyway, and v8's own entry in this
    /// list is the ruling: <i>"A PropKind ordinal is a data-shape change of the worst kind across
    /// builds: a stale client receiving an unknown kind falls through NetworkedProp.Init's switch
    /// to the Crate default and renders the wrong object instead of failing loudly, which is
    /// exactly what this field exists to convert into a refused connection."</i> A v15 peer in a
    /// v16 session would stand in an aisle of cereal boxes and see wooden crates — and, since
    /// SFX-1 resolves a prop's sound from its shape, would hear them as wooden crates too. That
    /// is a divergence, not a cosmetic gap. Nothing in <c>NetCodec</c> hashes or range-checks the
    /// enum, which was checked rather than assumed; the bump is on the meaning, as v6, v9, v10
    /// and v13 were.</para>
    ///
    /// <para><b>v16's second reason (2026-09-19, MATCH-1, folded in at INT-0B).</b> Five fields
    /// appended to <c>HideSeekWireTally</c> — <c>MatchOver</c>, <c>MatchIndex</c>,
    /// <c>WinnerPeerId</c>, <c>HiderTotal</c>, <c>SeekerTotal</c> — which widen the round
    /// channel's tally message and change what <c>Unpack</c> must read. MATCH-1 deliberately did
    /// not bump for them and said so in <c>HideSeekWire</c>'s class doc, on the reasoning that a
    /// wave shipping as one build should spend exactly one bump; this is that bump, and it is
    /// shared rather than doubled. <b>There is no v17 owed for this wave</b>: SFX-2 is already on
    /// 16 and BTN-1 puts nothing new on any wire. A lane that widens a message after this one
    /// takes 17 and writes its own paragraph here.</para>
    ///
    /// <para><b>v16's third reason (2026-09-19, SFX-2, folded in at INT-1).</b> A sixth argument
    /// on <c>PropManager.ApplyPropState</c> — the <c>PropRelease</c> byte
    /// (<c>None</c>/<c>Dropped</c>/<c>Placed</c>/<c>Thrown</c>) — which widens the one funnel
    /// every peer's held-by-peer view is written through, plus a new unreliable stream on
    /// <see cref="PropImpactChannel"/> (22). SFX-2 branched off SFX-1 with the bump already
    /// spent and deliberately did not bump again, writing the reason into its own handoff; the
    /// argument-count change is a data-shape change that would demand a bump on its own, and
    /// this is where it is recorded. <b>Three reasons, one bump.</b> A v15 peer never reaches
    /// any of them: it is refused at the handshake, which is the whole point.
    /// <c>PropReleaseWire.Decode</c> maps an unknown ordinal to <c>None</c> — silence, never a
    /// wrong sound — so a FUTURE peer that appends a fifth verb degrades quietly rather than
    /// playing the wrong material, and that is a property of the decoder rather than a reason to
    /// skip a bump.</para></summary>
    public const int ProtocolVersion = 16;

    /// <summary>Steam lobby scope tag — keeps this title's room codes from colliding with any
    /// other title developing against the same (shared Spacewar) App ID. Per-title value:
    /// change it with the App ID when you ship. Also the SteamLobby directory key.
    ///
    /// <para>Set to <c>super-find-great-deal</c> at the fork (BASE-1, 2026-09-19). Watis World
    /// ships <c>mp-foundation</c>, so a room code from one game can no longer resolve to a lobby
    /// of the other while both develop against the same Spacewar App ID. The App ID itself is
    /// deliberately untouched.</para></summary>
    public const string GameTag = "super-find-great-deal";

    /// <summary>Fallback Steam App ID when neither --steam-app-id nor steam_appid.txt is set:
    /// Valve's Spacewar test app. Swap for your real App ID at ship time (see SteamService).</summary>
    public const uint FallbackSteamAppId = 480;

    // --- Transport privacy -------------------------------------------------------
    /// <summary>
    /// Value applied to Steam's <c>k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable</c> on every
    /// P2P listen socket and connection. <b>0 = ICE disabled = Valve's relay only</b>, so peers
    /// never learn each other's IP addresses.
    ///
    /// WHY IT IS SET AT ALL. Passing no options leaves this at <c>-1</c> (Default), which defers
    /// to a setting in each player's own Steam client. Two players who both have "always share my
    /// IP" get a direct route with mutually visible addresses — a privacy property this game
    /// would be making no promise about, decided by a checkbox neither of them touched here.
    ///
    /// THE TRAP. This field is a bitmask of PERMITTED ICE CANDIDATE TYPES, not a mode selector.
    /// <c>1</c> is <c>_Relay</c>, meaning "allow TURN-relayed ICE candidates" — it enables part of
    /// the thing you were trying to turn off. Only <c>0</c> means no ICE at all. Do not "fix" this
    /// to 1.
    ///
    /// THE TRADE. Relay costs roughly 1-3s of extra connect time; Valve publishes no steady-state
    /// figure, and the relay is sometimes *faster* than a direct route because it rides their
    /// backbone. This is a knob rather than a literal so that trade can be settled by a playtest
    /// with real ping numbers instead of by argument — set it to -1 to restore the old behaviour.
    /// Nothing measured it before this was turned on, so nothing was traded away knowingly.
    /// </summary>
    public const int IceEnable = 0;

    // --- Capacity ----------------------------------------------------------------
    /// <summary>Hard cap on players per match, enforced server-side at the auth boundary. Every
    /// budget (draw calls, movers, voice streams, bandwidth) is calibrated to this number.</summary>
    public const int MaxPlayers = 6;

    /// <summary>Transport-level connection ceiling, above <see cref="MaxPlayers"/> only so the
    /// server can accept an over-cap connection far enough to reject it with a clean reason.</summary>
    public const int MaxClients = 16;

    /// <summary>Default UDP port for the ENet transport (LAN / CI / Practice).</summary>
    public const int DefaultPort = 7777;

    // --- Simulation timing -------------------------------------------------------
    /// <summary>Fixed simulation rate (Hz). The whole authority/prediction loop is deterministic
    /// against this — client and server must agree, so it is a protocol-level value.</summary>
    public const float TickRate = 60f;
    public const float TickDelta = 1f / TickRate;

    /// <summary>Server broadcasts an avatar snapshot every Nth simulation tick (2 ⇒ 30 Hz).</summary>
    public const int SnapshotIntervalTicks = 2;

    // --- Reliable RPC channel assignments ---------------------------------------
    // Distinct channels keep unrelated streams from head-of-line-blocking each other. Voice and
    // the loose-prop stream ride their own so a burst on one never delays movement/handshake.
    //
    // THE LADDER, in one place, because a duplicate value here is invisible: it compiles clean,
    // nothing goes red, and the only symptom is two unrelated streams blocking each other in a
    // playtest. That has already happened once (two systems on two branches both claimed 11 and a
    // textual merge produced two differently-named constants with the same value; one of them was
    // renumbered on 2026-08-11).
    //
    //   2 Voice · 3 Move · 4 Prop · 5 Cycle · 6 Run · 10 Water · 11 Incapacity · 13 Sight
    //   16 Flashlight · 17 Honk · 21 Round · 22 PropImpact
    //
    // The gaps (7-9, 12, 14, 15) belonged to systems that were cut from this build; they are left
    // unassigned rather than compacted so the surviving numbers keep their history. 18-20 are
    // likewise unclaimed: 21 was RESERVED for the round loop in the design this game's round came
    // from and the packet that landed it here names 21 explicitly, so the reservation was honoured
    // rather than compacted down to 18 — a channel number that moved between a design doc and the
    // code is exactly the kind of drift this ladder exists to stop. SFX-2 took 22 on 2026-09-19
    // (PropImpactChannel), after checking this ladder on EVERY wave-2 branch on the remote rather
    // than on its own base — the discipline INT-0 paid for when three lanes claimed 7896. NEXT
    // FREE CHANNEL IS 23 (18-20 are available to a lane that wants a low number and does not mind
    // the gap).
    //
    // Two systems written on different branches will both reach for the next number they can see,
    // so check this ladder against the tip you are merging into, not against the tip you branched
    // from. ENet is created with maxChannels = 0 (see NetworkManager.StartServer), which means
    // ENet's own maximum, so there is no ceiling to be near here — the constraint is uniqueness,
    // not supply.
    public const int VoiceChannel = 2;
    public const int MoveChannel = 3;
    public const int PropChannel = 4;
    public const int CycleChannel = 5;
    /// <summary>RunDriver's typed phase-crossing/run-end/reset events (L1, Issue #104) — its
    /// own channel so a burst of these never head-of-line-blocks (or gets blocked by) the
    /// unrelated cycle/prop/voice streams, same reasoning as every channel above it.</summary>
    public const int RunChannel = 6;

    /// <summary>WaterService's per-peer chill/soaked/phase stream, its five-event splash contract
    /// and its late-join dump (W2, lake-water contract). Its own channel for a reason specific to
    /// what it carries: the chill broadcast is a periodic unreliable trickle that runs for
    /// <i>minutes</i> at a time while a player is in the lake, and the sputter-out events that
    /// interleave with it are reliable and must land in order relative to each other. Sharing a
    /// reliable channel would put a two-minute swim's worth of chatter in front of that channel's
    /// ordered traffic.</summary>
    public const int WaterChannel = 10;

    /// <summary>IncapacitationService's failure-state event stream and its late-join dump (phase
    /// 1c, beta plan §10). Its own channel for the reason every channel above got one, sharpened
    /// by what this particular stream is: these are the messages that tell a client somebody is
    /// down, and the run's only hard loss condition is derived from them. They are reliable and
    /// low-volume, and putting them behind the water channel's minutes-long unreliable chill
    /// trickle — or behind the prop channel's scatter burst, which this very system triggers —
    /// would let "your whole group is down" arrive late behind the noise its own cause generated.</summary>
    public const int IncapacityChannel = 11;

    /// <summary><c>PlayerSightService</c>'s per-player sight-range table: the 5 Hz unreliable
    /// broadcast and the reliable late-join sync (canon fact 4, the parity law). Its own channel for
    /// a reason specific to its shape: this stream is a <i>continuous unreliable trickle</i> that
    /// never stops for the whole session, five messages a second from the moment the world loads.
    /// Sharing any reliable channel would put a permanent drip in front of that channel's ordered
    /// traffic; sharing <see cref="CycleChannel"/> — the other periodic unreliable stream, and the
    /// one this system's own input comes from — would let a sight table and the phase it was
    /// computed against be reordered relative to each other. Dropping a sample here costs 0.2 s of
    /// staleness and nothing else, which is exactly the property that makes it safe to isolate on
    /// its own unreliable lane.
    ///
    /// <para><b>No protocol bump, deliberately.</b> This adds a brand-new node with brand-new
    /// methods and no change to any existing message's shape, and the mismatched-build failure it
    /// produces is fail-<i>blind</i>: a peer with no <c>PlayerSightService</c> to dispatch to never
    /// becomes <c>Synced</c>, and an unsynced peer reads <c>PlayerSightCurve.DarkFloorM</c> rather
    /// than a wrong number. That is the direction <see cref="ProtocolVersion"/> exists to force,
    /// arrived at structurally instead of by refusing the connection. Bumping is still cheap if a
    /// build ever ships to two audiences at once — it is one line here — and it is called out
    /// rather than assumed.</para></summary>
    public const int SightChannel = 13;

    /// <summary>The player's flashlight (NIGHT-2, 2026-08-29): one reliable message per toggle
    /// press, plus the late-join dump. The quietest stream in this list — a player flips a pocket
    /// light a handful of times a night, at human cadence, and there is no continuous component at
    /// all. Isolation costs one integer, and it keeps a toggle from ever landing inside another
    /// system's order-dependent burst.
    ///
    /// <para><b>No protocol bump, on <see cref="SightChannel"/>'s reasoning.</b> This is a new node
    /// with new methods and no change to any existing message's shape, and the mismatched-build
    /// failure is fail-<i>dark</i>: a peer with no <c>FlashlightManager</c> to dispatch to renders
    /// nobody's glow, which is the state the game starts in rather than a wrong one.</para></summary>
    public const int FlashlightChannel = 16;

    /// <summary>The goose honk (HONK-1, 2026-09-04): the mic-less player's stand-in for proximity
    /// voice. One reliable, payload-free message per press, plus the server's in-earshot broadcast.
    /// Quieter than the flashlight even — a press is capped at one per
    /// <c>HonkConfig.CooldownSec</c> and there is no continuous component and no late-join dump at
    /// all, because a honk is an EVENT with no state to be behind on.
    ///
    /// <para><b>Its own channel for the reason every channel above got one</b>, sharpened by what
    /// this one carries: a honk is a social beat, and a beat that arrives late has already missed
    /// the moment it was answering. Riding <see cref="VoiceChannel"/> — the obvious neighbour —
    /// would be the worst of the options available: that channel is UNRELIABLE by construction and
    /// carries a 50 Hz stream per talker, so a honk on it would be both droppable and queued
    /// behind the very traffic it substitutes for.</para>
    ///
    /// <para><b>No protocol bump, on <see cref="SightChannel"/>'s and
    /// <see cref="FlashlightChannel"/>'s reasoning.</b> This is a new node with new methods and no
    /// change to the shape of any existing message, and the mismatched-build failure is
    /// fail-<i>harmless</i>: a peer with no <c>HonkManager</c> to dispatch to plays no honk, which
    /// is the state the game was in yesterday rather than a wrong one. It is not silent — Godot
    /// prints a node-not-found error on that peer for each honk it cannot dispatch — but no game
    /// state diverges, and a build old enough to hit it is a build from before this feature
    /// existed. <see cref="ProtocolVersion"/>
    /// is deliberately UNTOUCHED here — the bump for this wave belongs to NET-1's crew-state wire
    /// change and must not be spent on a system that structurally does not need one.</para></summary>
    public const int HonkChannel = 17;

    /// <summary>The hide-seek round's replicated state (ROUND-1, 2026-09-19): one absolute
    /// message carrying the phase, the clock, both roles, every score and the card, broadcast when
    /// it changes and sent once to every joining peer. <c>HideSeekDriver</c> is the only sender.
    ///
    /// <para><b>Its own channel for the reason every channel above got one</b>, sharpened by what
    /// this one carries: this is the stream that says WHICH ROOM YOU ARE ABOUT TO BE IN. A phase
    /// message arriving behind somebody else's burst is a player who is teleported before their
    /// HUD knows why, which reads exactly like the desync this game is built not to have. It is
    /// reliable, ordered, and low-volume — the wire quantises its clock to tenths, so "broadcast
    /// on change" is about ten messages a second at its very busiest and nothing at all while the
    /// players stand in the holding room.</para>
    ///
    /// <para><b>21, not 18</b>, and deliberately: see the ladder comment above. The number was
    /// reserved for this system in the design it came from.</para>
    ///
    /// <para><b>No protocol bump</b>, on <see cref="SightChannel"/>'s and
    /// <see cref="HonkChannel"/>'s reasoning: a new node with new methods and no change to the
    /// shape of any existing message. The mismatched-build case cannot arise anyway — nothing has
    /// ever shipped from this repo, and <see cref="ProtocolVersion"/> was already bumped to 15 at
    /// the fork for the one refusal that matters (a Watis World client reaching this
    /// server).</para></summary>
    public const int RoundChannel = 21;

    /// <summary>The server's prop-impact announcement (SFX-2, 2026-09-19): one small unreliable
    /// message per accepted contact — prop id, one intensity byte, the position — so that every
    /// peer hears a can come off a shelf and not only the host.
    ///
    /// <para><b>Its own channel, and NOT <see cref="PropChannel"/>, although that is the obvious
    /// neighbour.</b> Channel 4 carries two things already: the reliable ordered
    /// <c>ApplyPropState</c> stream (grab, release, settle — the messages a player's hand
    /// depends on) and the 30 Hz unreliable loose-transform stream. A burst of forty impacts
    /// cannot be allowed to sit in front of a grab, which is the whole reason the packet says to
    /// keep this out of the state RPC's lane; and sharing the unreliable loose stream would mean
    /// an impact competing for delivery with the very transform updates that describe the
    /// collapse it belongs to.</para>
    ///
    /// <para><b>Unreliable, deliberately, and this is the direction a cosmetic one-shot must
    /// fail in.</b> Reliable-on-its-own-channel would also keep clear of the grab, but it would
    /// buy delivery with ORDER and RETRANSMISSION: a shelf going over would queue its impacts,
    /// and an impact that arrives 300 ms late is worse than one that never arrives — it is a
    /// clank from a can that has already stopped rolling, in a game whose seeker is navigating
    /// by sound. <c>HonkChannel</c>'s own entry above states the same rule for the same reason
    /// ("a beat that arrives late has already missed the moment it was answering"). Nothing here
    /// is state: a lost impact costs one sound and no peer diverges.</para>
    ///
    /// <para><b>22, the next free number</b> — see the ladder comment above, and note that it
    /// was checked against every wave-2 branch on the remote rather than against this lane's
    /// base, which is the discipline INT-0 paid for.</para>
    ///
    /// <para><b>No protocol bump for the channel</b>, on <c>SightChannel</c>'s reasoning: a new
    /// method on an existing node, and a peer with no <c>PropImpact</c> to dispatch to plays no
    /// impact, which is the state SFX-1 shipped rather than a wrong one. <see cref="ProtocolVersion"/>
    /// is 16 for this wave already and SFX-2 deliberately does not bump it again — see the v16
    /// entry.</para></summary>
    public const int PropImpactChannel = 22;

    // --- Input codec bounds ------------------------------------------------------
    /// <summary>How many recent inputs each packet re-sends for loss tolerance (client) and the
    /// hard upper bound the server accepts before rejecting a packet as malformed.</summary>
    public const int InputRedundancy = 4;
    public const int MaxInputEntries = 8;

    // --- World -------------------------------------------------------------------
    /// <summary>The flat-world default out-of-bounds floor. Kept as a <c>const</c> so the value a
    /// world falls back to is still a compile-time fact; the value actually enforced is
    /// <see cref="KillPlaneY"/>, which a world may override.</summary>
    public const float KillPlaneYDefault = -30f;

    /// <summary>Shared out-of-bounds floor: anything (avatar or prop) below this Y is recovered.
    ///
    /// <para><b>Per-world, not global.</b> It used to be a <c>const</c> whose own doc said "a
    /// flat-world default — a world with real vertical extent should override per-world (tracked
    /// in RISK-AUDIT-2026-07-12.md 3.2)", and there was no way to do that: a level with a tower
    /// and a sub-grade room got the flat-world number whether it fitted or not, and the only
    /// remedies were moving the level or moving every other level with it. This is that override,
    /// in the shape <see cref="Sail.Game.Run.RespawnService.VoidKillY"/> already uses one level
    /// up — the world sets its own floor at load.</para>
    ///
    /// <para><b>Lifetime rule, and it is the whole footgun.</b> A world that sets this MUST put it
    /// back with <see cref="ResetKillPlaneToDefault"/> when it leaves the tree, or its floor
    /// follows the player into the next level. Set it from the world's own load path — the path
    /// every peer runs — never from a gameplay event: this value is read by the SERVER's
    /// out-of-bounds recovery (<c>SandboxAvatar.ServerTick</c>, <c>PropManager</c>) and by
    /// client-side lifetime checks, so the two must agree by construction. It is never replicated
    /// and nothing a client asserts reaches it.</para>
    ///
    /// <para><b>No world overrides it today</b> — every level in the repo runs on
    /// <see cref="KillPlaneYDefault"/>, so this change moves nothing. The bubble test's TV room
    /// was raised above the default instead (BubbleTestLayout.TvRoomAnchor, −20, Talon
    /// 2026-08-28); wiring that level to this override is BT-0's file to change, not this
    /// one's.</para></summary>
    public static float KillPlaneY { get; set; } = KillPlaneYDefault;

    /// <summary>Puts <see cref="KillPlaneY"/> back to the flat-world default. Every world that
    /// overrides the floor calls this from <c>_ExitTree</c>.</summary>
    public static void ResetKillPlaneToDefault() => KillPlaneY = KillPlaneYDefault;
}
