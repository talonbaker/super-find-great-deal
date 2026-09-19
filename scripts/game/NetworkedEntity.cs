using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game;

/// <summary>
/// Reusable base for a <b>server-simulated, snapshot-interpolated</b> networked entity with
/// <b>no client prediction</b> — the predator, and any future AI/NPC. It is the deliberately
/// smaller sibling of <see cref="Sandbox.SandboxAvatar"/>: the player avatar adds an input
/// queue, local prediction, and reconciliation on top of this same snapshot machinery, because
/// only the owning player needs zero-latency response. An NPC nobody owns needs none of that —
/// the server is the sole simulator and every client simply renders the interpolated result.
///
/// This exists so the predator does <b>not</b> copy-paste the avatar's 900 lines to get the
/// reusable half (RISK-AUDIT-2026-07-12.md 5.1e). It reuses the exact same, already-tested
/// <see cref="NetCodec"/> snapshot codec and <see cref="SnapshotBuffer"/> interpolation the
/// avatar's remote proxies use — including the epoch-based teleport signal and the
/// reordered-stale-snapshot guard — so an entity built on this inherits those correctness
/// properties for free.
///
/// A subclass implements one method — <see cref="ServerSimulate"/> — where it moves the body
/// (steering, chase/flee, <c>MoveAndSlide</c>). Everything else (authority role, per-tick
/// broadcast cadence, remote interpolation, teleport/respawn via epoch bump) is handled here.
///
/// NOTE: the snapshot/broadcast plumbing is compile-verified but its <i>replication behaviour</i>
/// can only be validated against a live multiplayer session — exercise it in the netstep/bot
/// harness before the first NPC ships.
/// </summary>
public abstract partial class NetworkedEntity : CharacterBody3D
{
    protected enum EntityRole { Offline, ServerSim, RemoteProxy }

    private EntityRole _role = EntityRole.Offline;
    private uint _serverTick;
    private int _sinceSnapshot;
    private byte _epoch;
    private SnapshotBuffer? _snapshots;
    private bool _hasRendered;
    private float _maxRenderStep;

    /// <summary>True on the authoritative server instance of this entity — the only place its
    /// behaviour actually runs. Subclasses can gate server-only state on this.</summary>
    protected bool IsServer => _role is EntityRole.ServerSim or EntityRole.Offline;

    /// <summary>Where the entity should be drawn this frame. Server/offline: the body itself;
    /// remote proxy: the interpolated render position. Cosmetics (nameplates, shadows, FX) that
    /// track the entity per frame must read this, mirroring the avatar's RenderGlobalPosition
    /// contract.</summary>
    public Vector3 RenderGlobalPosition => GlobalPosition;

    /// <summary>False for a remote proxy until its first snapshot has actually landed and been
    /// rendered; always true for the server/offline instance (it IS the live truth from the
    /// instant it exists — nothing to wait for). Same "unknown state must never be
    /// indistinguishable from a real zero value" discipline <see cref="Sandbox.SandboxAvatar"/>'s
    /// remote synchronizers and every other Synced flag in this codebase (CycleDriver.Synced,
    /// RunDriver.Synced) already use — a freshly-spawned remote proxy's
    /// default, un-teleported <see cref="RenderGlobalPosition"/> is (0,0,0), which is a real,
    /// plausible-looking world position, not an obviously-fake sentinel. A dynamically-spawned
    /// entity (e.g. a projectile spawned mid-session at the shot moment, unlike
    /// <c>TestWanderer</c>'s spawn-at-session-start) can have a caller (BotHarness) observe it in
    /// the single frame between spawn-replication landing and its first snapshot arriving; a
    /// caller that skips samples where this is false never mistakes that gap for a real position.</summary>
    public bool Synced => _role != EntityRole.RemoteProxy || _hasRendered;

    // --- Configuration (call once, right after spawn) ---------------------------------

    /// <summary>Authoritative server instance: runs <see cref="ServerSimulate"/> every physics
    /// tick and broadcasts a snapshot every <see cref="NetProfile.SnapshotIntervalTicks"/>.</summary>
    public void ConfigureServer()
    {
        _role = EntityRole.ServerSim;
        SetPhysicsProcess(true);
        SetProcess(false);
    }

    /// <summary>Remote proxy on a non-authoritative peer: runs no physics, renders the
    /// interpolated snapshot trajectory (~<see cref="SnapshotBuffer.InterpDelayTicks"/> behind).</summary>
    public void ConfigureRemote()
    {
        _role = EntityRole.RemoteProxy;
        _snapshots = new SnapshotBuffer();
        SetPhysicsProcess(false);
        SetProcess(true);
    }

    /// <summary>Single-player / headless test: simulates locally with no broadcast.</summary>
    public void ConfigureOffline()
    {
        _role = EntityRole.Offline;
        SetPhysicsProcess(true);
        SetProcess(false);
    }

    // --- The one thing a subclass writes ----------------------------------------------

    /// <summary>Server-authoritative behaviour for one fixed sim step: read the world (positions,
    /// prop state — all server-owned), steer, and move the body (e.g. set <see cref="Velocity"/>
    /// then <c>MoveAndSlide()</c>). Runs on the server and in offline mode; never on a remote
    /// proxy. Keep it deterministic against <see cref="NetProfile.TickDelta"/>.</summary>
    protected abstract void ServerSimulate(double delta);

    // --- Fixed-step simulation + broadcast (server/offline) ---------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_role == EntityRole.RemoteProxy)
            return; // remotes render in _Process; they never simulate

        ServerSimulate(delta);

        if (_role != EntityRole.ServerSim)
            return; // offline: simulate only, no wire traffic

        _serverTick++;
        if (++_sinceSnapshot < NetProfile.SnapshotIntervalTicks)
            return;
        _sinceSnapshot = 0;
        BroadcastSnapshot();
    }

    private void BroadcastSnapshot()
    {
        var state = new MoveState
        {
            Position = GlobalPosition,
            Velocity = Velocity,
            Yaw = Rotation.Y,
            Grounded = IsOnFloor(),
            // Coyote/jump-buffer fields are avatar-specific; an NPC leaves them zero. The 8 wasted
            // bytes buy 100% reuse of the proven snapshot codec + interpolation — a deliberate trade.
        };
        byte[] packet = NetCodec.PackSnapshot(new NetCodec.Snapshot(_serverTick, _epoch, 0, state));
        Rpc(MethodName.ReceiveEntitySnapshot, packet);
    }

    /// <summary>Server: relocate the entity (respawn, warp) so every remote SNAPS instead of
    /// interpolating across the gap — bumps the snapshot epoch, exactly like the avatar's
    /// reset-to-spawn. No-op off the server.</summary>
    protected void ServerTeleport(Vector3 position)
    {
        if (_role != EntityRole.ServerSim && _role != EntityRole.Offline)
            return;
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _epoch++; // remotes read the epoch change as a teleport and snap (SnapshotBuffer)
    }

    // --- Remote interpolation (per frame, non-authoritative peers) ---------------------

    public override void _Process(double delta)
    {
        if (_role != EntityRole.RemoteProxy)
            return;
        if (_snapshots!.Sample(delta) is not SnapshotBuffer.RenderSample s)
            return; // nothing authoritative yet — hold at spawn pose

        if (_hasRendered && !s.Teleported)
        {
            float step = GlobalPosition.DistanceTo(s.Position);
            if (step > _maxRenderStep)
                _maxRenderStep = step;
        }
        _hasRendered = true;

        GlobalPosition = s.Position;
        Rotation = new Vector3(0, s.Yaw, 0);
        OnRemoteRendered(delta, s);
    }

    /// <summary>Hook for a remote proxy to drive purely-cosmetic reconstruction from the
    /// interpolated sample (walk animation, lean, FX) — never gameplay state, which is
    /// server-authoritative. Default: nothing. Mirrors the avatar's "visuals reconstructed
    /// locally from state, never transform-streamed" principle.</summary>
    protected virtual void OnRemoteRendered(double delta, SnapshotBuffer.RenderSample sample) { }

    /// <summary>Peak frame-to-frame rendered movement since the last call (teleport epochs
    /// excluded) — the smoothness invariant a test asserts stays bounded under loss/jitter.</summary>
    public float TakeMaxRenderStep()
    {
        float peak = _maxRenderStep;
        _maxRenderStep = 0;
        return peak;
    }

    // --- Snapshot receive (server -> everyone) ----------------------------------------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetProfile.MoveChannel)]
    private void ReceiveEntitySnapshot(byte[] packet)
    {
        if (NetCodec.UnpackSnapshot(packet) is not NetCodec.Snapshot snap)
            return; // malformed — dropped, never fatal (same discipline as the avatar path)
        if (_role != EntityRole.RemoteProxy || _snapshots is null)
            return;
        // Honor the CI network simulator's latency/loss the same way the avatar does, so the
        // smoothness harness exercises this path under the same conditions.
        if (NetSim.Instance is NetSim sim)
            sim.Queue(() => { if (IsInstanceValid(this) && IsInsideTree()) _snapshots?.Add(snap); });
        else
            _snapshots.Add(snap);
    }
}
