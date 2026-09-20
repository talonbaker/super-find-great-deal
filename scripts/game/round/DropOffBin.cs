using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The drop-off bin in the search room</b> — an open-top box with a sensor in it. The seeker
/// puts the odd object in and the round ends. Anything else gets a buzz and stays exactly where
/// it fell.
///
/// <para><b>The server decides; clients are told.</b> The sensor is read on the server only, the
/// target's identity is <c>RoundControls</c>'s, and both outcomes come back as one broadcast. A
/// client that decided for itself would be a client that can end a round.</para>
///
/// <para><b>"In the bin" is a LEVEL, and a DELIVERY.</b> ROUND-1's interface is explicit that
/// <see cref="IRoundFactSource.TargetInDropOff"/> must not pulse — a one-tick edge loses the
/// find to a dropped tick. So this latches. What it latches on is the target ENTERING while the
/// round is Seeking, not merely being inside: the hider is alone in this room during Hiding and
/// could otherwise hide the object in the bin, which would end the seek on the first tick of it.
/// A bin measures a delivery, not a location, and that one word removes the degenerate hide
/// without ejecting anything (the packet is explicit that nothing is ejected).</para>
///
/// <para><b>It builds nothing.</b> Walls, floor, sensor and lamp are all authored in
/// <c>SearchRoom.tscn</c> — <c>SupermarketWorldSelfTest</c> counts that room's packed nodes
/// against its live ones, so a bin assembled in <c>_Ready</c> would turn the suite red.</para>
/// </summary>
public partial class DropOffBin : Node3D
{
    /// <summary>Group, for a level author and a capture script. <c>RoundControls</c> finds it by
    /// type.</summary>
    public const string Group = "dropoff_bin";

    /// <summary>How long the lamp holds its reject flash. The packet's half second.</summary>
    public const double RejectFlashSec = 0.5;

    /// <summary>One buzz per prop per this long. A crate that bounces on the bin's lip can cross
    /// the sensor several times in a second, and four buzzes for one throw reads as a fault
    /// rather than as an answer.</summary>
    public const double RejectCooldownSec = 1.0;

    private static readonly Color LampIdle = new(0.16f, 0.17f, 0.18f);
    private static readonly Color LampGood = new(0.28f, 0.95f, 0.40f);
    private static readonly Color LampBad = new(0.94f, 0.26f, 0.18f);

    /// <summary><b>The target has been delivered.</b> Latched; cleared only at the round
    /// boundary. This is what <c>RoundControls</c>'s probe reads.</summary>
    public bool TargetDelivered { get; private set; }

    /// <summary>How many wrong props have been buzzed this round. Instrumentation: a suite
    /// asserting "the wrong prop did NOT end the round" needs to know the wrong prop actually
    /// arrived, or the assertion passes on a throw that missed.</summary>
    public int RejectsThisRound { get; private set; }

    private Area3D? _sensor;
    private MeshInstance3D? _lamp;
    private StandardMaterial3D? _lampMaterial;

    private bool _isServer;
    private Func<int>? _target;
    private Func<HideSeekPhase>? _phase;
    private Func<int>? _seeker;

    private readonly HashSet<int> _inside = new();
    private readonly Dictionary<int, double> _lastRejectAt = new();
    private double _clock;
    private double _flashLeft;

    public override void _Ready()
    {
        AddToGroup(Group);
        _sensor = GetNodeOrNull<Area3D>("Sensor");
        _lamp = GetNodeOrNull<MeshInstance3D>("Lamp");
        if (_sensor is null || _lamp is null)
        {
            GD.PushError($"[bin] {Name}: expects authored children Sensor (Area3D) and Lamp "
                         + $"(MeshInstance3D) — sensor={_sensor != null} lamp={_lamp != null}. "
                         + "See SearchRoom.tscn's DropOffBin for the shape.");
            return;
        }
        // Per-instance material, same reason as RoundButton's: a SubResource authored in a room
        // scene is shared by every instantiation of that scene, and the world self-test
        // instantiates each room twice. Duplicating a resource creates no nodes.
        _lampMaterial = (_lamp.MaterialOverride as StandardMaterial3D)?.Duplicate() as StandardMaterial3D;
        if (_lampMaterial != null)
            _lamp.MaterialOverride = _lampMaterial;
        Paint(LampIdle, 0.08f);
    }

    /// <summary>Server-side wiring, from <c>RoundControls</c>. Three closures rather than three
    /// values: the target changes every round, the phase changes five times a round, and the
    /// seeker swaps at every reset — a captured value would be stale by the time the bin was
    /// used.</summary>
    internal void SetupServer(Func<int> target, Func<HideSeekPhase> phase, Func<int> seeker)
    {
        _isServer = true;
        _target = target;
        _phase = phase;
        _seeker = seeker;
    }

    /// <summary>The round boundary: forget the delivery and the buzz cooldowns.</summary>
    internal void ClearForNewRound()
    {
        TargetDelivered = false;
        RejectsThisRound = 0;
        _lastRejectAt.Clear();
        _inside.Clear();
        Rpc(MethodName.ApplyBinLamp, false, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        _clock += delta;
        if (_flashLeft > 0.0)
        {
            _flashLeft -= delta;
            if (_flashLeft <= 0.0)
                Paint(TargetDelivered ? LampGood : LampIdle, TargetDelivered ? 1.6f : 0.08f);
        }
        if (!_isServer || _sensor is null)
            return;
        ScanServer();
    }

    /// <summary>
    /// <b>One pass over what is in the bin.</b> Polled rather than signal-driven, and that is a
    /// robustness choice with a measured shape behind it: a held prop's collider is DISABLED
    /// (<c>Carryable.OnPickedUp</c>), so the interesting case is a shape being re-enabled while
    /// already overlapping the sensor — the exact case where a <c>body_entered</c> edge is
    /// easiest to lose. <see cref="Area3D.GetOverlappingBodies"/> reads the monitor list the
    /// physics server already maintains; it is not a shape query, so a tick-rate poll of one
    /// small area costs nothing measurable.
    /// </summary>
    private void ScanServer()
    {
        HideSeekPhase phase = _phase?.Invoke() ?? HideSeekPhase.Holding;
        int target = _target?.Invoke() ?? -1;

        var now = new HashSet<int>();
        foreach (Node3D body in _sensor!.GetOverlappingBodies())
        {
            if (body is not Carryable || body.GetParent() is not NetworkedProp prop || prop.PropId <= 0)
                continue;
            now.Add(prop.PropId);
            if (_inside.Contains(prop.PropId))
                continue;
            OnEntered(prop.PropId, target, phase);
        }
        _inside.Clear();
        foreach (int id in now)
            _inside.Add(id);
    }

    private void OnEntered(int propId, int targetPropId, HideSeekPhase phase)
    {
        // THE BIN IS ONLY LIVE DURING THE SEEK. Outside it there is nothing to deliver and
        // nobody to buzz: the holding room's rack is where objects are chosen, and the props a
        // cost probe shoves across this floor are not a player making a mistake.
        if (phase != HideSeekPhase.Seeking)
            return;

        if (propId == targetPropId && targetPropId >= 0)
        {
            if (TargetDelivered)
                return;
            TargetDelivered = true;
            GD.Print($"[bin] the target (prop {propId}) was delivered — the round ends on the "
                     + "next server tick");
            Rpc(MethodName.ApplyBinLamp, true, propId, _seeker?.Invoke() ?? 0);
            return;
        }

        if (_lastRejectAt.TryGetValue(propId, out double last) && _clock - last < RejectCooldownSec)
            return;
        _lastRejectAt[propId] = _clock;
        RejectsThisRound++;
        GD.Print($"[bin] prop {propId} is not the one (target {targetPropId}) — "
                 + $"buzz #{RejectsThisRound}, nothing moved");
        Rpc(MethodName.ApplyBinLamp, false, propId, _seeker?.Invoke() ?? 0);
    }

    /// <summary>
    /// Server → everyone, <c>CallLocal</c>. <b>The lamp is everyone's and the sound is not.</b>
    /// The chime on an accepted delivery plays on every peer — it is the one moment the hider is
    /// entitled to hear, half a second before DOOR-1's door arrives — and the buzz on a wrong
    /// prop plays only for the seeker, because it is an answer to something only they did.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.RoundChannel, CallLocal = true)]
    private void ApplyBinLamp(bool accepted, int propId, int seekerPeerId)
    {
        if (propId == 0 && !accepted && seekerPeerId == 0)
        {
            // The round-boundary reset: back to idle, no sound.
            TargetDelivered = false;
            _flashLeft = 0.0;
            Paint(LampIdle, 0.08f);
            return;
        }

        _flashLeft = accepted ? 0.0 : RejectFlashSec;
        bool mine = seekerPeerId != 0 && seekerPeerId == (int)Multiplayer.GetUniqueId();
        // ONE LINE PER PEER, because "a chime on every peer" is a claim a headless suite can only
        // check by reading every peer's own stdout. The server's [bin] lines above are what the
        // SERVER decided; this is what each peer was told.
        GD.Print($"[bin] lamp {(accepted ? "GREEN (chime)" : "RED (buzz for the seeker)")} "
                 + $"prop={propId}{(mine ? " (this peer is the seeker)" : string.Empty)}");
        if (accepted)
        {
            TargetDelivered = true;
            Paint(LampGood, 1.6f);
            SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Chirp),
                volumeDb: -3f, pitchJitter: 0.02f, maxDistance: 30f);
            return;
        }

        Paint(LampBad, 1.6f);
        if (mine)
            SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Buzzer),
                volumeDb: -5f, pitchJitter: 0.02f, maxDistance: 14f);
    }

    private void Paint(Color c, float energy)
    {
        if (_lampMaterial is null)
            return;
        _lampMaterial.AlbedoColor = c;
        _lampMaterial.EmissionEnabled = true;
        _lampMaterial.Emission = c;
        _lampMaterial.EmissionEnergyMultiplier = energy;
    }
}
