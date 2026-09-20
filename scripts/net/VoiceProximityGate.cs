using System.Collections.Generic;
using Godot;
using MpFoundation.Voice;

namespace MpFoundation.Net;

/// <summary>
/// THE SWITCH plus the tuned radii for the server-side voice relay proximity gate.
///
/// WHAT IT IS FOR. The 2026-08-07 perf audit measured host upstream at six players at
/// ~1.5 Mbps, of which ~two thirds is voice — and <see cref="VoiceConfig.ProximityMaxDistance"/>
/// is a HARD cutoff (VoiceSpeaker sets AudioStreamPlayer3D.MaxDistance = 24 m), so a packet
/// relayed to someone 200 m away buys literal silence at full price. Culling those at the
/// relay is worth roughly 120 → 48 KB/s of host upstream.
///
/// WHY IT SHIPS OFF. VoiceManager.SubmitVoice documented a deliberate architectural stance —
/// "the server never inspects positions for voice, and attenuation is the client's job" — and
/// enabling this gate REVERSES that stance. That is Talon's call, not an agent's. It is built,
/// tested and measured; flipping <see cref="EnabledByDefault"/> to <c>true</c> is the whole
/// activation procedure.
///
/// THE PA HAZARD, stated where someone will read it. The PA route (VoiceRoute.Pa, no distance
/// falloff at all — see VoiceSpeaker.SetRoute) is resolved on the CLIENT, through
/// VoiceManager.PaResolver. A server that gates on distance without knowing who is on PA will
/// silently mute the intercom. The exemption therefore reads the SAME PaResolver hook on the
/// server (see VoiceManager.PaResolver's doc comment): PA is a pure function of replicated
/// state, and the server is the source of that state, so the same predicate is resolvable on
/// both sides. Anyone wiring a PA route MUST wire it on the server too, or turn this gate off.
/// The server logs which way it went at first gated relay so a silenced PA is diagnosable from
/// a log instead of a playtest.
/// </summary>
public static class VoiceProximityGate
{
    /// <summary>THE SWITCH. <c>false</c> = today's behaviour, byte for byte: every accepted
    /// peer gets every voice packet. Flip to <c>true</c> to ship the proximity cull.
    /// (<see cref="Enabled"/> is what the relay actually reads; a test/measurement launch flag
    /// may override it at runtime — see LaunchOptions.VoiceGate.)</summary>
    public const bool EnabledByDefault = false;

    /// <summary>Start relaying when the pair is within this. 1.25× the 24 m hard audibility
    /// cutoff: the margin exists so the relay is ALREADY flowing by the time a listener walks
    /// into earshot, which is what makes VoiceSpeaker's 60 ms jitter prebuffer refill
    /// inaudible — the listener still has ~6 m (≈1.2 s at walk speed) of approach left before
    /// attenuation lets them hear anything at all.</summary>
    public const float EnterRadiusM = VoiceConfig.ProximityMaxDistance * 1.25f; // 30 m

    /// <summary>Stop relaying only past this. The 10 m band between enter and exit is the
    /// hysteresis: a player oscillating across the boundary flips the relay at most once per
    /// 10 m of travel instead of once per footstep, so the jitter buffer is never torn down
    /// and rebuilt in a loop. Both radii sit outside the 24 m audibility cutoff, so NEITHER
    /// transition is audible — the hysteresis protects the buffer, the margin protects the
    /// ear.</summary>
    public const float ExitRadiusM = VoiceConfig.ProximityMaxDistance * 1.667f; // 40 m

    /// <summary>How stale a cached server-side avatar position may be before the gate
    /// re-reads it. Voice arrives at 50 Hz per talker; re-reading Node3D.GlobalPosition for
    /// every (talker, listener) pair on every packet would be ~1,800 engine interop calls per
    /// second at six players. At 50 ms a position is at most ~0.25 m stale at walk speed,
    /// which is nothing against a 30 m gate with a 10 m hysteresis band — so this trades an
    /// irrelevant amount of accuracy for a 15× cut in interop.</summary>
    public const double PositionSampleIntervalSec = 0.05;

    /// <summary>THE PER-WORLD ENABLE (BT-0, program decision D5, 2026-08-27).
    ///
    /// <para>Talon, ruling Q4: <i>"The gray hub section exists specifically to test proximity
    /// voice chat — leaving the gate off would silently cancel the one section built to test
    /// it."</i> The Bubble Test's third question is whether proximity voice holds up for 1–6
    /// players, and a gate that is off tests nothing: every peer would receive every packet and
    /// only client-side 3D attenuation would give the proximity feel, which is the behaviour that
    /// already ships and that nobody needs a level to evaluate.</para>
    ///
    /// <para><b>Pure and static on purpose</b> — no Godot state, no <c>NetworkManager</c> — so
    /// <c>tests/unit</c> can assert both arms without a runtime ("enabled when the world is the
    /// supermarket and not otherwise").
    /// It is a per-world <i>default</i>, not a lock: <c>--voice-gate on|off</c> still wins,
    /// because that flag writes <see cref="Enabled"/> and an explicit write beats the default
    /// (see the setter).</para>
    ///
    /// <para><b>This does NOT reverse the stance the class doc describes for the game at
    /// large.</b> <see cref="EnabledByDefault"/> is still <c>false</c>, and the CI scaffolding
    /// worlds still relay everything to everyone. Flipping the stance for the whole game remains
    /// Talon's call and remains one edit to that constant.</para>
    ///
    /// <para><b>On by default for the supermarket (BASE-1, 2026-09-19)</b>, because this level's
    /// geometry is built around it: the three rooms are 40 m apart and the proximity cutoff is
    /// 24 m, so an ungated relay would let a hider hear the seeker through two solid walls and
    /// the whole round would be over. VOICE-1 owns the other half of the answer — the cross-room
    /// PA/intercom route — and it must read the same per-talker resolver on the server that the
    /// client does, or the PA is silently muted here.</para></summary>
    public static bool DefaultForWorld(string worldId) =>
        EnabledByDefault || worldId == Game.World.SupermarketWorld.WorldId;

    /// <summary>What the relay actually reads. Resolved from <see cref="DefaultForWorld"/> against
    /// the world this process was launched with, unless something has written it explicitly
    /// (<c>--voice-gate on|off</c>, or a test exercising both arms in one build). Server side only
    /// — a client never relays anything.
    ///
    /// <para>(Process-scoped on purpose, not session-scoped: the override comes from a launch
    /// flag, and a server process that tore a match down and started another should still be
    /// running the mode it was launched in.)</para>
    ///
    /// <para>Resolved lazily rather than assigned once at start-up, because there is no single
    /// moment that is reliably "after the world id is known and before the first voice packet" —
    /// the world is chosen in <c>Gameplay._Ready</c> and this is read from the relay path. The
    /// answer is cached per world id, so the string comparison happens once rather than at 50 Hz
    /// per talker-listener pair.</para></summary>
    public static bool Enabled
    {
        get
        {
            if (_explicit.HasValue) return _explicit.Value;
            string world = NetworkManager.Instance?.Options?.World ?? "";
            if (_cachedFor is null || !string.Equals(_cachedFor, world, System.StringComparison.Ordinal))
            {
                _cachedFor = world;
                _cached = DefaultForWorld(world);
            }
            return _cached;
        }
        set => _explicit = value;
    }

    private static bool? _explicit;
    private static string? _cachedFor;
    private static bool _cached = EnabledByDefault;
}

/// <summary>
/// The gate's decision core: pure, allocation-free after warm-up, and unit-testable without a
/// Godot runtime (tests/unit/VoiceProximityGateTests.cs). One instance lives on the server's
/// VoiceManager; it holds the per-(talker, listener) hysteresis latch and nothing else.
///
/// MECHANICS-BIBLE boundary discipline, spelled out because this IS a boundary condition:
///   - The two radii are asymmetric and the latch is per ORDERED pair. Voice is directional
///     (A talking to B is a different stream from B talking to A) and the pairs are opened and
///     closed independently, so a stale latch on one can never mute the other.
///   - Unknown position FAILS OPEN. If either avatar cannot be resolved — the spawn/despawn
///     race VoiceManager.EmitPosFor already documents — the packet is relayed. The failure a
///     player would report is "I could not hear anyone", so the safe direction is loud, not
///     quiet, and a fail-open costs at most a few frames of one extra stream.
///   - A fail-open does NOT write the latch. It is an "I don't know", not an observation, so
///     the next resolvable frame is judged against the ENTER radius exactly as if the pair had
///     never been evaluated. Latching on a non-observation would let one spawn-frame hold a
///     100 m pair open until it drifted past 40 m.
///   - PA exemption is checked before distance and also does not write the latch, so switching
///     the PA off mid-sentence leaves the pair to be judged on its real distance rather than
///     inheriting an "open" the PA put there.
/// </summary>
public sealed class VoiceRelayDecider
{
    // Ordered (talker, listener) pairs currently relaying. Packed into one long so the set is
    // a plain HashSet<long> with no tuple boxing or custom comparer; at the 6-player cap this
    // holds at most 30 entries.
    private readonly HashSet<long> _open = new();

    /// <summary>Packets relayed since the last <see cref="TakeCounters"/> (both gated-and-passed
    /// and PA-exempt), for the bandwidth instrumentation.</summary>
    private long _relayed;
    private long _gated;
    private long _paExempt;

    internal static long PairKey(int talker, int listener) =>
        ((long)(uint)talker << 32) | (uint)listener;

    /// <summary>Should this one packet from <paramref name="talkerId"/> be relayed to
    /// <paramref name="listenerId"/>? Both positions are nullable on purpose — see the
    /// fail-open note on the class.</summary>
    public bool ShouldRelay(int talkerId, int listenerId, Vector3? talkerPos, Vector3? listenerPos,
        bool paExempt)
    {
        if (paExempt)
        {
            _paExempt++;
            _relayed++;
            return true;
        }
        if (talkerPos is not Vector3 a || listenerPos is not Vector3 b)
        {
            _relayed++;
            return true; // fail open, and deliberately do NOT latch
        }

        long key = PairKey(talkerId, listenerId);
        bool wasOpen = _open.Contains(key);
        // 3D distance, matching AudioStreamPlayer3D's own 3D MaxDistance — a listener one
        // storey below a talker is as far away to the mixer as one 3 m along the ground.
        float dist = a.DistanceTo(b);
        bool open = wasOpen
            ? dist <= VoiceProximityGate.ExitRadiusM
            : dist <= VoiceProximityGate.EnterRadiusM;

        if (open != wasOpen)
        {
            if (open)
                _open.Add(key);
            else
                _open.Remove(key);
        }

        if (open)
            _relayed++;
        else
            _gated++;
        return open;
    }

    /// <summary>Drops every latch involving <paramref name="peerId"/> in either role. Called on
    /// peer disconnect: ENet peer ids are recycled, and a recycled id must be judged fresh
    /// rather than inherit a departed player's open pair (the same reasoning VoiceManager
    /// already applies to its avatar cache and its peer-id mute entries).</summary>
    public void ForgetPeer(int peerId)
    {
        if (_open.Count == 0)
            return;
        _open.RemoveWhere(key => (int)(key >> 32) == peerId || (int)key == peerId);
    }

    /// <summary>Session reset. Every latch belonged to the previous match's peer set.</summary>
    public void Clear()
    {
        _open.Clear();
        _relayed = 0;
        _gated = 0;
        _paExempt = 0;
    }

    /// <summary>Pairs currently latched open — test/telemetry seam.</summary>
    public int OpenPairCount => _open.Count;

    /// <summary>Reads and resets the relay counters. Mirrors ENetConnection.PopStatistic's
    /// read-and-reset contract deliberately, so NetStatsLogger can put both on one line
    /// covering the same interval without two different accumulation rules.</summary>
    public (long Relayed, long Gated, long PaExempt) TakeCounters()
    {
        var result = (_relayed, _gated, _paExempt);
        _relayed = 0;
        _gated = 0;
        _paExempt = 0;
        return result;
    }
}
