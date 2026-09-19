using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.Honk;

/// <summary>
/// <b>The goose honk: the mic-less player's proximity voice.</b> HONK-1, 2026-09-04, closing
/// Talon's ask at the end of the playtest — <i>"a simple 'sound' … which will act like a voice
/// input from players who don't have microphones or don't want to speak … mapped to 'H'"</i> —
/// and the line in the foreword he is shipping beside it: <i>"for the mic-less or shy, pressing
/// 'H' will act as a kind of pseudo-proximity voice."</i>
///
/// <para><b>That second sentence is the whole specification, and it rules out the easy build.</b>
/// A honk only its presser can hear is worthless: the point is that OTHER PLAYERS hear it,
/// positionally, where they would have heard that player's voice. So this is a replicated verb,
/// not a local sound effect, and everything below follows from that.</para>
///
/// <para><b>An EVENT, not a stream.</b> Host upstream at the six-player cap was measured at
/// ~1.5 Mbps, two thirds of it voice (2026-08-07 perf audit). A honk therefore sends the TRIGGER
/// and nothing else — no audio, no position, no name — and every receiver synthesises the same
/// 0.42 s waveform locally from <c>SfxLab.GooseHonkPcm</c>. The wire cost of a honk is one
/// payload-free reliable message plus one broadcast of a single int, capped by the cooldown at
/// ≤1.67 per player per second. Against voice's fifty packets a second per talker it is noise
/// under the noise floor.</para>
///
/// <para><b>Server-authoritative in this codebase's own sense</b> (ARCHITECTURE: <i>"a client can
/// only ever say 'here is my stick input'"</i>). Three separate things make a forged honk
/// impossible rather than merely discouraged:
/// <list type="number">
/// <item><see cref="RequestHonk"/> <b>carries no payload at all</b>, so there is no field in which
/// a client could name another peer. The honker's identity is
/// <c>Multiplayer.GetRemoteSenderId()</c>, which the transport supplies and the sender cannot
/// write. This is <c>FlashlightManager.RequestToggle</c>'s shape, deliberately.</item>
/// <item><b>No position is ever sent.</b> Where a honk comes out is resolved on each receiver from
/// the honker's replicated avatar — the same node the receiver is already drawing — so a claimed
/// position is not merely rejected, it is unsayable. This is <c>VoiceManager.EmitPosFor</c>'s
/// shape, deliberately.</item>
/// <item><see cref="PlayHonk"/> is <c>RpcMode.Authority</c>, so a client that aims it straight at
/// another client is refused by the multiplayer API before this class sees it — and the body
/// re-checks the sender anyway, because <c>SceneMultiplayer</c> relays client→client RPCs by
/// default and defence in depth is what <c>VoiceManager.SubmitVoice</c> already does here.</item>
/// </list></para>
///
/// <para><b>No local prediction.</b> A press produces nothing until the server answers, which is
/// <c>FlashlightManager.ClientRequestToggle</c>'s stance and <c>PropManager.ClientRequestGrab</c>'s
/// before it. The honker hears their own honk from the server's broadcast like everyone else. The
/// alternative — playing locally on press — would let a player hear a honk that the cooldown
/// refused and nobody else received, which is the worst possible feedback for a social verb: it
/// teaches them the button worked when it did not.</para>
///
/// <para><b>Late join: nothing to send, deliberately.</b> Every other replicated manager here has a
/// dump (<c>FlashlightManager.SendFlashlightStateTo</c>, <c>PropManager.SendDumpTo</c>) and their
/// doc comments explain what a joiner would otherwise render wrongly. A honk is transient: there
/// is no state to be behind on, and a honk that happened before you joined is a honk you correctly
/// did not hear. The absence of a dump here is the decision, not the omission.</para>
///
/// <para><b>INTERACTION-BIBLE §8.1, scope:</b> <c>proximity</c>, declared rather than arrived at.
/// <b>§8.2, the redundant-channel rule:</b> this consequence is audio-only, and §8.2's own table
/// is what permits it — <i>social communication between players</i> is the row marked "may be
/// lost", as against run-critical world state which is not. <b>§3, physical correspondence:</b>
/// there is no visual tell on the honking body, and that is a real gap rather than a resolved
/// one; it is reported in the packet's report rather than built, because the packet puts visuals
/// out of scope.</para>
///
/// <para><b>Audio budget:</b> this allocates no <c>AudioStreamPlayer3D</c> of its own. It rents
/// from <c>SfxLab</c>'s existing 14-slot one-shot pool, because
/// <c>AudioVoiceBudget</c> reserves all 24 of its documented ceiling and has zero headroom — a
/// second pool is the thing that budget exists to prevent. A six-player honk pile-up therefore
/// degrades through <c>SfxLab</c>'s oldest-shot-stolen policy, which is already the decided
/// behaviour for exactly this case.</para>
/// </summary>
public partial class HonkManager : Node
{
    public const string NodeName = "HonkManager";

    /// <summary>The live honk system, or null in a scene that has none. Same static-instance seam
    /// every replicated manager here provides; null-check it.</summary>
    public static HonkManager? Instance { get; private set; }

    /// <summary>The cooldown latch. On the server this is the authority; on a client it is a local
    /// copy that keeps a held key from spending a packet a frame. Same type, two roles — see
    /// <see cref="HonkGate"/> — and <b>two different tolerances</b>, which is the whole reason it is
    /// rebuilt in <see cref="Setup"/> rather than constructed once here: the server's copy must be
    /// the looser of the pair or network jitter eats every second honk of a held key. See
    /// <see cref="HonkConfig.ServerJitterToleranceSec"/>.</summary>
    private HonkGate _gate = new();

    /// <summary>Honker peer id -> honks this peer actually PLAYED here. Bumped only after BOTH
    /// gates pass — a resolvable source and an in-range listener — so it means "heard", never
    /// "arrived". A headless peer counts too: it takes every decision and stops one line short of
    /// the mixer, which is the only reason a bot can witness any of this.</summary>
    private readonly Dictionary<int, long> _heard = new();

    /// <summary>Honker peer id -> honk messages that ARRIVED here, in earshot or not. The pair
    /// with <see cref="_heard"/> is what makes a headless absence check mean something: a far peer
    /// with <c>received &gt; 0</c> and <c>heard == 0</c> proves the range rule culled it, where
    /// <c>received == 0</c> alone would equally be a broken wire.</summary>
    private readonly Dictionary<int, long> _received = new();

    private bool _isServer;
    private System.Func<int, Node3D?>? _avatarResolver;

    /// <summary>Raised on every peer that actually plays a honk. Nothing subscribes yet; it exists
    /// so a later visual tell (INTERACTION-BIBLE §3) or a HUD pip subscribes rather than polling —
    /// the same seam <c>FlashlightManager.FlashlightToggled</c> left open for the same reason.</summary>
    public event System.Action<int>? Honked;

    /// <summary>Positions come from the server's OWN authoritative avatar nodes on the server, and
    /// from the receiver's own replicated ones on a client — never from anything a client asserted.
    /// The identical seam <c>FlashlightManager.Setup</c> and <c>PropManager.AvatarResolver</c>
    /// take.</summary>
    public void Setup(bool isServer, System.Func<int, Node3D?> avatarResolver)
    {
        _isServer = isServer;
        _avatarResolver = avatarResolver;
        // A fresh latch per session, with THIS side's tolerance: strict on a client (so it spaces
        // its sends at the honest cooldown) and forgiving on the server (so a send that arrives a
        // few milliseconds early through the network is still granted).
        _gate = new HonkGate { ToleranceSec = isServer ? HonkConfig.ServerJitterToleranceSec : 0 };
        _heard.Clear();
        _received.Clear();
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // --- The verb ------------------------------------------------------------------------------

    /// <summary>Client-side entry point: one <c>H</c> press, at most one request.
    ///
    /// <para>The local cooldown check is a WIRE saving, not the rule — a client that deletes this
    /// line still cannot honk faster than <see cref="HonkConfig.CooldownSec"/>, because the server
    /// runs the same latch and its answer is the only one that produces a sound anywhere. What it
    /// buys is that a player holding <c>H</c> down sends ~1.7 messages a second instead of 60.</para>
    ///
    /// <para>Returns whether a request was actually sent, so the input reader can tell a suppressed
    /// press from a sent one without reaching into the gate.</para></summary>
    public bool ClientRequestHonk()
    {
        int self = (int)Multiplayer.GetUniqueId();
        double now = Time.GetTicksMsec() / 1000.0;
        if (_isServer)
        {
            // The host takes the local path rather than an RPC to itself — FlashlightManager's
            // shape. ServerHonk runs the authoritative latch, so the host is not privileged here.
            return ServerHonk(self);
        }
        if (_gate.RemainingSec(self, now) > 0)
            return false;
        // Advance the local copy so a held key is rate-limited here too. Deliberately NOT treated
        // as permission: the server may still refuse this press (its clock and this one are not
        // the same clock), in which case no honk happens anywhere, which is the correct outcome.
        _gate.TryHonk(self, now);
        RpcId(1, MethodName.RequestHonk);
        return true;
    }

    /// <summary>Client → server: "I honked." <b>Carries no payload</b> — see the class doc's
    /// forgery note for why that is the security property rather than a size optimisation.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.HonkChannel)]
    private void RequestHonk()
    {
        // SceneMultiplayer relays client->client RPCs by default, so a hostile client could aim
        // this at another client. Non-servers ignore it outright (VoiceManager.SubmitVoice's
        // guard, same reason).
        if (!_isServer || !Multiplayer.IsServer())
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0)
            return;
        if (!NetworkManager.Instance.IsPeerAccepted(peer))
        {
            ServerLog.Warn("honk from unauthenticated peer dropped", $"peer={peer}");
            return;
        }
        ServerHonk(peer);
    }

    /// <summary>Server: grant or refuse one peer's honk, and broadcast the grant.
    ///
    /// <b>The single funnel.</b> Every path that produces a honk anywhere goes through here, so
    /// there is no route that broadcasts without first passing the cooldown — which is what makes
    /// <see cref="HonkGate.TryHonk"/>'s idempotency reachable rather than merely present
    /// (MECHANICS-BIBLE §4). Returns whether the honk was granted.</summary>
    public bool ServerHonk(int peerId)
    {
        if (!_isServer || peerId <= 0)
            return false;
        double now = Time.GetTicksMsec() / 1000.0;
        if (!_gate.TryHonk(peerId, now))
        {
            LogThrottleDrop(peerId, now);
            return false;
        }
        // Broadcast to everyone including the server-as-player: each receiver decides for itself
        // whether it is in earshot, because that is a question about what THAT machine hears and
        // needs no authority (HonkGate's class doc carries the full argument, and the stance it is
        // avoiding). CallLocal so a listening host runs the same receive path as every client
        // rather than a second copy of it.
        Rpc(MethodName.PlayHonk, peerId);
        return true;
    }

    /// <summary>Server → every peer: "peer N honked." One int, and it is an identity rather than a
    /// position — see the class doc.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.HonkChannel, CallLocal = true)]
    private void PlayHonk(int honkerId)
    {
        // Defence in depth behind RpcMode.Authority: on a client, anything that reaches here must
        // have come from peer 1. A forged PlayHonk aimed client-to-client is refused by the
        // multiplayer API first and by this line second.
        if (!Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() != 1)
            return;
        if (honkerId <= 0)
            return;

        _received[honkerId] = _received.GetValueOrDefault(honkerId) + 1;

        // NO SOURCE, NO HONK — and nothing counted. The honker's avatar has not spawned here yet,
        // or has just despawned; there is literally nowhere for the sound to come out of, which is
        // the same call VoiceSpeaker.Tick makes when its emit position is null. Deliberately NOT
        // routed through InEarshot's fail-open: that fail-open is about not knowing how FAR a honk
        // is, and answering "loud" is the safe direction for that. Not knowing WHERE it is has no
        // safe-loud answer. Counting it as heard would also be a lie the suite reads as a failure
        // of the range rule (Run-HonkTest cell 1 asserts a far peer's heard count is 0), so a
        // transient spawn race would produce a red blaming the wrong thing.
        if (HeadOf(honkerId) is not Vector3 pos)
            return;

        // The listener's own position, on the other hand, IS fail-open: unknown means "I cannot
        // tell how far away I am", and the safe direction there is to hear it. The mixer's own
        // MaxDistance is still behind this as the final word.
        if (!HonkGate.InEarshot(pos, HeadOf((int)Multiplayer.GetUniqueId())))
            return;

        _heard[honkerId] = _heard.GetValueOrDefault(honkerId) + 1;
        Honked?.Invoke(honkerId);

        if (NetworkManager.Instance is { IsHeadless: true })
            return; // mechanism proven and counted; no audio device to feed on a headless peer

        // The one §4-compliant playback path. maxDistance and unitSize are voice's own tuned
        // numbers by alias (HonkConfig), so the mixer's falloff and this class's earshot test are
        // literally the same two constants, and retuning voice range moves both.
        SfxLab.PlayStream3D(this, pos, SfxLab.Get(Sfx.GooseHonk),
            volumeDb: HonkConfig.VolumeDb,
            pitchJitter: HonkConfig.PitchJitter,
            maxDistance: HonkConfig.AudibleRangeM,
            unitSize: HonkConfig.UnitSizeM);
    }

    // --- Lifecycle ------------------------------------------------------------------------------

    /// <summary>A peer left. Their cooldown latch goes with them: ENet peer ids are recycled, and
    /// an inherited latch would silently eat the next occupant's first honk. The receive counters
    /// deliberately SURVIVE — they are session-scoped telemetry ("how much did I ever hear from
    /// X", read by the bot harness after the honker has already gone) and are cleared with the
    /// session in <see cref="Setup"/>, exactly as <c>VoiceManager</c>'s are.</summary>
    public void OnPeerLeft(int peerId) => _gate.ForgetPeer(peerId);

    // --- Queries / telemetry --------------------------------------------------------------------

    /// <summary>Per-honker counts of honks this peer PLAYED, string-keyed for JSON. The bot harness
    /// logs these so the whole path is provable headlessly.</summary>
    public Dictionary<string, long> GetHeardCounts() => Stringify(_heard);

    /// <summary>Per-honker counts of honk messages that ARRIVED here, in earshot or not. Paired
    /// with <see cref="GetHeardCounts"/> this is what turns "the far peer stayed silent" into a
    /// claim about the range rule rather than about the wire.</summary>
    public Dictionary<string, long> GetReceivedCounts() => Stringify(_received);

    /// <summary>Server-side grant/refusal totals. <c>Throttled</c> is the direct proof that the
    /// anti-spam rule fired, as opposed to a honk that never arrived.</summary>
    public (long Granted, long Throttled) ServerCounters => _gate.Counters;

    private static Dictionary<string, long> Stringify(Dictionary<int, long> source)
    {
        var snapshot = new Dictionary<string, long>(source.Count);
        foreach (KeyValuePair<int, long> kv in source)
            snapshot[kv.Key.ToString()] = kv.Value;
        return snapshot;
    }

    /// <summary>Where a honk leaves a body, or null when that avatar is not resolvable right now
    /// (the spawn/despawn race <c>VoiceManager.EmitPosFor</c> documents). Shares
    /// <c>VoiceManager.HeadPositionOf</c> rather than copying it, so a honk and a voice always
    /// leave the same point of the same body.</summary>
    private Vector3? HeadOf(int peerId)
    {
        Node3D? avatar = _avatarResolver?.Invoke(peerId);
        if (avatar == null || !GodotObject.IsInstanceValid(avatar) || !avatar.IsInsideTree())
            return null;
        return Voice.VoiceManager.HeadPositionOf(avatar);
    }

    private double _lastThrottleLogSec = double.NegativeInfinity;

    /// <summary>One line per second at most, whatever the flood. A mashing player is the EXPECTED
    /// case here, not an attack, so the log exists to make the cooldown's work visible to a test
    /// and to a diagnosis — not to shout about it. Same throttle idea as
    /// <c>VoiceManager.LogRejectThrottled</c>.</summary>
    private void LogThrottleDrop(int peerId, double now)
    {
        if (now - _lastThrottleLogSec < 1.0)
            return;
        _lastThrottleLogSec = now;
        (long granted, long throttled) = _gate.Counters;
        ServerLog.Info("honk cooldown: press dropped",
            $"peer={peerId} cooldown={HonkConfig.CooldownSec:F2}s granted={granted} throttled={throttled}");
    }

    // --- Test seam ------------------------------------------------------------------------------

    /// <summary><b>Test-only forgery probe</b> (<c>--honk-forge</c>). Fires the broadcast RPC
    /// straight from a CLIENT at another client, naming a third peer as the honker — the exact
    /// attack the class doc claims is impossible. It exists so that claim is a measured fact
    /// rather than a reading of the attribute: <c>RpcMode.Authority</c> must refuse it, and
    /// <c>Run-HonkTest.ps1</c> asserts the victim's counters never move.
    ///
    /// <para>Precedent: <c>VoiceTestSender</c>'s oversize/garbage modes exist for the same reason —
    /// a security property nothing ever tries to violate is a comment, not a property.</para></summary>
    public void TestForgeHonk(int targetPeer, int claimedHonkerId) =>
        RpcId(targetPeer, MethodName.PlayHonk, claimedHonkerId);

    /// <summary><b>Test-only "modified client" probe</b> (<c>--honk-flood</c>): sends the request
    /// to the server WITHOUT consulting the local cooldown copy, which is exactly what a player
    /// who deleted that check from their build would do.
    ///
    /// <para>It exists because the shipped path cannot exercise the rule that matters. A real
    /// mashing player is stopped by <see cref="ClientRequestHonk"/>'s local latch before a packet
    /// ever leaves the machine — measured, in this suite's first green run: seven presses produced
    /// two honks and the server logged nothing at all, because it was never asked. So the local
    /// latch was proving itself and the AUTHORITATIVE latch had never run once. This is what makes
    /// the server-side half of the anti-spam rule a measured fact instead of an unexercised
    /// branch. Precedent: <c>--voice-flood</c>, for the same reason.</para></summary>
    public void TestRawRequest()
    {
        if (_isServer)
            ServerHonk((int)Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.RequestHonk);
    }
}
