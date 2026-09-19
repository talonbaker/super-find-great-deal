using System;
using System.Collections.Generic;
using Godot;
using MpFoundation;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.Run;

/// <summary>Why a player died. Append-only — these ride an RPC.</summary>
public enum RespawnCause : byte
{
    /// <summary>Unspecified; the fallback so a caller can never fail to name one.</summary>
    Unknown = 0,

    /// <summary><b>Walked off the edge of the world.</b> Talon, 2026-08-22: <i>"assume the caveman
    /// believes they will 'fall off the edge of the earth' like in the old legends. This will be
    /// the case."</i> The legend is true.</summary>
    OffTheEdge = 1,

    /// <summary>Fell below the world and kept going.</summary>
    Void = 2,

    /// <summary><b>Went under and stayed under.</b> Talon, 2026-08-29: <i>"the water should kill
    /// the player after about three seconds"</i>. A death with a respawn, not a faint (the
    /// PLAYTEST-2 ruling), and comic under the caveman register exactly like the other two — the
    /// same loft-and-spin beat, nothing gory, nobody taken.</summary>
    Drowned = 3,
}

/// <summary>
/// <b>Death and respawn — the one genuinely new subsystem in PLAYTEST-1.</b>
///
/// <para>Respawn did not exist anywhere in this codebase before this. Two comments mentioned it
/// (<c>Gameplay.cs</c>, <c>IncapacitationService.cs</c>) and nothing implemented it. The packet is
/// explicit that <i>every future death will use this</i>, so it is built simply but with a real
/// seam: <see cref="ServerKill"/> is the single entry point, it takes a cause, and a drowning, a
/// Breaker or a Long One can call it tomorrow without touching this file.</para>
///
/// <para><b>Deliberately NOT routed through <see cref="Failure.IncapacitationService"/>.</b> That
/// service owns Knocked Out / Frozen — states that are <i>recoverable by teammates</i> and that
/// feed <c>AllConnectedIncapacitated</c>, the run's hard-loss signal. Walking off the map is
/// neither: it is instant, it is self-inflicted, and a solo player testing the edge must not end
/// the run by doing it once. Borrowing that machine would have made the map edge a loss condition
/// by accident, which is exactly the class of unasked-for consequence the bible check exists to
/// catch.</para>
///
/// <para><b>DROP, not delete — and the packet asked for this to be costed before it was chosen.</b>
/// The manifest assumed losing everything was the cheap option because drop-on-death "is not
/// built". Measured: <see cref="PropManager.ReleaseHeldBy"/> already exists, is public and
/// server-side, empties BOTH carry slots and the arms, rebroadcasts the held-by registers, and leaves each
/// item at the body's own transform — its own doc calls it "a reusable release funnel". Drop is one
/// call. Delete would have meant enumerating held props and consuming each, which is <i>more</i>
/// code. So the playtest gets the real design (Talon's drowning ruling: the player drops
/// everything and the items stay as scenery) at a lower cost than the shortcut.</para>
///
/// <para><b>Server-authoritative.</b> Only the server tests positions, only the server kills, and
/// the teleport is <see cref="SandboxAvatar.ServerTeleportTo"/> — which bumps the movement epoch so
/// remote peers SNAP across the gap instead of sweeping a body 400 m across the map.</para>
/// </summary>
public sealed partial class RespawnService : Node
{
    public const string NodeName = "RespawnService";

    /// <summary>How often the out-of-bounds test runs, seconds. Ten times a second is far finer
    /// than a player can cross the boundary in, and it keeps this off the physics tick.</summary>
    private const float ScanPeriodSec = 0.1f;

    public static RespawnService? Instance { get; private set; }

    /// <summary>Live avatars. Supplied by <c>Gameplay</c> rather than searched for, the same
    /// pattern <c>IncapacitationService</c> uses.</summary>
    public Func<IEnumerable<SandboxAvatar>>? Avatars { get; set; }

    /// <summary>Where the dead come back — the world's respawn landmark (in PLAYTEST-1, the one
    /// that world's landmark table named as spawn/respawn/deposit).</summary>
    public Func<Vector3>? RespawnPoint { get; set; }

    /// <summary>The drop funnel. Null-safe: with no PropManager a death simply keeps its cargo.</summary>
    public PropManager? Props { get; set; }

    /// <summary>Horizontal distance from the origin past which a player is off the map.</summary>
    public float OffMapRadiusM { get; set; } = 214f;

    /// <summary>Y below which a falling player has left the world.</summary>
    public float VoidKillY { get; set; } = -80f;

    /// <summary>How long the comic beat plays before the body reappears at the respawn point.</summary>
    public float DeathBeatSeconds { get; set; } = 2.0f;

    /// <summary>
    /// Seconds continuously submerged before a body drowns. Defaults to
    /// <see cref="Water.WaterGeometry.DrownAfterSec"/> (3.0) — the water contract owns the number,
    /// <see cref="DrowningClock"/> owns the clock and this is the knob onto it. Non-positive turns
    /// drowning off entirely, which is how a lab or a world with no lake opts out without a second
    /// flag.
    /// </summary>
    public float DrownSeconds
    {
        get => _drowning.DrownAfterSec;
        set => _drowning.DrownAfterSec = value;
    }

    /// <summary>Fired on every peer when someone dies: (peerId, cause).</summary>
    public event Action<int, RespawnCause>? Died;

    /// <summary>Fired on every peer when someone comes back: (peerId).</summary>
    public event Action<int>? Respawned;

    /// <summary>How many deaths this session, for the report and for BotHarness.</summary>
    public int DeathCount { get; private set; }

    private bool _isServer;
    private float _scanAccum;

    /// <summary>Peers mid-death, and when they come back. Keyed by peer so a second trigger while
    /// already dying is ignored rather than restarting the beat — a body tumbling past the edge
    /// crosses the boundary on many consecutive ticks, and without this every one of them would
    /// re-fire the death.</summary>
    private readonly Dictionary<int, double> _dyingUntil = new();

    /// <summary>The drowning clock (WATER-3). Server-only state: no client ever reports being
    /// under, exactly as the boundary scan never asks a client whether it has left the map. Split
    /// out of this class so the arithmetic is provable without a scene tree — see
    /// <see cref="DrowningClock"/>'s own doc for the precedent it follows.</summary>
    private readonly DrowningClock _drowning = new();

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Setup(bool isServer, Func<IEnumerable<SandboxAvatar>> avatars,
                      Func<Vector3> respawnPoint, PropManager? props)
    {
        _isServer = isServer;
        Avatars = avatars;
        RespawnPoint = respawnPoint;
        Props = props;
    }

    /// <summary>True while this peer is playing its death beat and cannot act.</summary>
    public bool IsDying(int peerId) => _dyingUntil.ContainsKey(peerId);

    /// <summary>
    /// Forget a peer entirely (they left). Mirrors <c>WaterService.ForgetPeer</c>, and is called
    /// from the same line of <c>Gameplay.OnPeerDisconnected</c> for the same stated reason: a
    /// reconnecting player resumes at their last authoritative position, so one who dropped while
    /// under water must start their three seconds again rather than resume 2.9 s into a breath
    /// they stopped holding.
    ///
    /// <para>It clears the death beat too. A peer who disconnects mid-beat would otherwise be
    /// "respawned" — teleporting an avatar that is no longer in the tree and broadcasting a
    /// respawn for somebody who is not in the session.</para>
    /// </summary>
    public void ForgetPeer(int peerId)
    {
        _dyingUntil.Remove(peerId);
        _drowning.Clear(peerId);
    }

    public override void _Process(double delta)
    {
        if (!_isServer) return;

        // Resolve anyone whose beat has finished, first — so a player killed and respawned inside
        // one frame budget still lands before the next boundary test looks at them.
        if (_dyingUntil.Count > 0)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            List<int>? done = null;
            foreach ((int peer, double at) in _dyingUntil)
            {
                if (now >= at) (done ??= new List<int>()).Add(peer);
            }
            if (done != null)
                foreach (int peer in done) FinishRespawn(peer);
        }

        _scanAccum += (float)delta;
        if (_scanAccum < ScanPeriodSec) return;
        // The REAL elapsed time since the last scan, not ScanPeriodSec: the accumulator only fires
        // at or past the period, so assuming exactly 0.1 s would run the drowning clock slow on a
        // loaded frame and turn "about three seconds" into four.
        float sinceLastScan = _scanAccum;
        _scanAccum = 0f;
        ScanForBoundaryCrossings(sinceLastScan);
    }

    private void ScanForBoundaryCrossings(float dt)
    {
        if (Avatars is null) return;
        foreach (SandboxAvatar a in Avatars())
        {
            int peer = a.OwnerPeerId;
            if (_dyingUntil.ContainsKey(peer)) continue;

            Vector3 p = a.GlobalPosition;
            if (p.Y < VoidKillY)
            {
                ServerKill(peer, RespawnCause.Void);
            }
            else if (MathF.Abs(p.X) > OffMapRadiusM || MathF.Abs(p.Z) > OffMapRadiusM)
            {
                ServerKill(peer, RespawnCause.OffTheEdge);
            }
            else if (TickDrowning(peer, p, dt))
            {
                ServerKill(peer, RespawnCause.Drowned);
            }
        }
    }

    /// <summary>
    /// <b>The drowning clock, and the whole of it.</b> Advances while the body is submerged and
    /// resets the instant it is not; true means this scan is the one that kills.
    ///
    /// <para><b>Submerged, not merely wet.</b> The predicate is
    /// <see cref="Water.WaterGeometry.IsSubmerged"/> — depth at or past the state machine's
    /// Swimming entry, i.e. out of your depth with the crown of the head at the waterline. Wading
    /// never starts the clock, which is what keeps the shallows the day-register comedy the water
    /// contract's §2 designed them to be, and what makes the lake read as a hazard with an edge
    /// rather than a kill plane.</para>
    ///
    /// <para><b>Reset, not decay</b>, and the rest of the arithmetic: see
    /// <see cref="DrowningClock"/>, which owns it and is proved to the boundary in
    /// <c>DrowningClockTests</c>.</para>
    ///
    /// <para><b>Bounded by the lake, and that ordering is load-bearing.</b>
    /// <c>WaterGeometry</c>'s lake is a finite footprint (WATER-3), so a body off the edge of the
    /// map is dry and falling, and the two branches above claim it as <see cref="RespawnCause.Void"/>
    /// or <see cref="RespawnCause.OffTheEdge"/>. Wired against the old half-plane this clock would
    /// have drowned every player who walked off the western edge, in mid-air, with no water
    /// rendered anywhere near them.</para>
    /// </summary>
    private bool TickDrowning(int peer, Vector3 feet, float dt) => _drowning.Tick(peer, feet, dt);

    /// <summary>
    /// <b>Kill a player. This is the seam every future death cause calls.</b>
    ///
    /// <para>Idempotent per peer while the beat is playing: a body tumbling out of bounds trips the
    /// boundary on every scan, and re-entering here would restart the beat forever.</para>
    /// </summary>
    public void ServerKill(int peerId, RespawnCause cause)
    {
        if (!_isServer || _dyingUntil.ContainsKey(peerId)) return;

        // Everything you were carrying stays where you fell. Items float and both the body and the
        // items stay as scenery — Talon's drowning ruling, and the reason this is drop rather than
        // delete (see the class doc for the cost that decided it).
        Props?.ReleaseHeldBy(peerId);

        // Whatever this death was, the body is about to be somewhere else. Clearing the drowning
        // clock here rather than in the drowning branch means every cause resets it — including a
        // teleport, a Void kill from inside the lake column, and a future creature — so a peer can
        // never respawn already three seconds into a breath they are no longer holding.
        _drowning.Clear(peerId);

        _dyingUntil[peerId] = Time.GetTicksMsec() / 1000.0 + DeathBeatSeconds;
        DeathCount++;

        Rpc(MethodName.BroadcastDied, peerId, (int)cause);
        OnDied(peerId, (int)cause); // the server plays it too; every peer sees the same beat
    }

    private void FinishRespawn(int peerId)
    {
        _dyingUntil.Remove(peerId);

        Vector3 target = RespawnPoint?.Invoke() ?? Vector3.Zero;
        SandboxAvatar? avatar = FindAvatar(peerId);
        // ServerTeleportTo, not a GlobalPosition write: it bumps the movement epoch so remote peers
        // SNAP to the new position instead of interpolating a body smoothly across 400 m of map.
        avatar?.ServerTeleportTo(target);

        Rpc(MethodName.BroadcastRespawned, peerId);
        OnRespawned(peerId);
    }

    private SandboxAvatar? FindAvatar(int peerId)
    {
        if (Avatars is null) return null;
        foreach (SandboxAvatar a in Avatars())
            if (a.OwnerPeerId == peerId) return a;
        return null;
    }

    // -------------------------------------------------------------------------------------------
    // The beat. Nothing here is gory and nobody is taken — the register law, which no packet
    // touches. What changed in W7-4 is that the comedy is now in the POSE rather than in the
    // trajectory: the body topples onto its back, a drowning sinks, and whatever it does it arrives
    // somewhere and holds there until it is put back. The curve itself lives in DeathBeatPose,
    // which owns the three rules and is proved to the boundary without a scene tree.
    // -------------------------------------------------------------------------------------------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastDied(int peerId, int cause) => OnDied(peerId, cause);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastRespawned(int peerId) => OnRespawned(peerId);

    private void OnDied(int peerId, int cause)
    {
        var c = (RespawnCause)cause;
        SandboxAvatar? avatar = FindAvatar(peerId);
        // Said out loud on every peer, WITH THE PLACE. "Nobody died" and "the death did not
        // replicate" look identical from inside the game, and this is the one line that tells them
        // apart in a log; W7-4 added the position because "where did that happen" is the first
        // question anyone asks of a death, and answering it needed a bespoke probe run until now.
        // Appended, never reshaped: the "died: <cause>" prefix is what callers grep.
        GD.Print($"[respawn] peer {peerId} died: {c}"
                 + (avatar != null ? $" at {avatar.GlobalPosition}" : ""));
        if (avatar != null)
            AddChild(new DeathBeat { Target = avatar, Seconds = DeathBeatSeconds, Cause = c });
        Died?.Invoke(peerId, c);
    }

    private void OnRespawned(int peerId)
    {
        GD.Print($"[respawn] peer {peerId} back at the respawn point");
        Respawned?.Invoke(peerId);
    }

    /// <summary>
    /// The death beat, as its own short-lived node so it cannot leak state between deaths.
    ///
    /// <para>Purely presentational — it declares a pose on the avatar's <i>visual</i> child and
    /// never touches the body's authoritative transform, so a client playing a beat can never
    /// disagree with the server about where anyone is standing. It declares that pose through
    /// <see cref="AvatarVisual.SetDeathPose"/> rather than writing the node, because the visual's
    /// root transform has three contributors and exactly one writer (W7-4; see the comment block
    /// on those fields for the bug that bought the rule).</para>
    ///
    /// <para><b>Same on every peer.</b> The shape is <see cref="DeathBeatPose"/>, a pure function
    /// of the replicated cause and normalised beat time. Nothing about it is sampled from local
    /// physics, so two clients watching the same body see the same beat.</para>
    /// </summary>
    private sealed partial class DeathBeat : Node
    {
        public SandboxAvatar? Target;
        public float Seconds = 2f;
        public RespawnCause Cause = RespawnCause.Unknown;

        private float _t;
        private AvatarVisual? _visual;

        public override void _Ready()
        {
            // By name only. SandboxAvatar builds its AvatarVisual as a child literally called
            // "Visual", and falling back to child 0 would sooner or later pose a CollisionShape3D
            // instead — a death beat that quietly moves the body's collider is a much worse bug
            // than a death beat that does not play.
            _visual = Target?.GetNodeOrNull<AvatarVisual>("Visual");
            if (_visual is null)
                GD.PushWarning("[respawn] no \"Visual\" child on the avatar; death plays no beat.");
        }

        public override void _Process(double delta)
        {
            _t += (float)delta;
            float u = Mathf.Clamp(_t / Mathf.Max(0.01f, Seconds), 0f, 1f);

            if (_visual != null && IsInstanceValid(_visual))
                _visual.SetDeathPose(DeathBeatPose.Sample(Cause, u));

            if (u >= 1f)
            {
                // Cleared, not left at the terminal pose. The respawn teleports this same body back
                // into play a frame or two from here, and a player who came back still lying on
                // their back would be the one failure mode worse than the one this packet fixed.
                if (_visual != null && IsInstanceValid(_visual))
                    _visual.SetDeathPose(DeathBeatPose.Pose.Rest);
                QueueFree();
            }
        }

        public override void _ExitTree()
        {
            // Belt and braces for the paths that do not run to u >= 1: a peer disconnecting
            // mid-beat, a world teardown, a scene change. A freed beat must never leave a pose
            // behind on a body that outlives it.
            if (_visual != null && IsInstanceValid(_visual))
                _visual.SetDeathPose(DeathBeatPose.Pose.Rest);
        }
    }
}
