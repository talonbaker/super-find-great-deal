using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// <b>Which section is a player standing in?</b> One <see cref="Area3D"/> per section, authored in
/// <c>BubbleTest.tscn</c> at the footprint program §4 gives that section, existing for exactly one
/// reason: program §6 item 6 — <i>"the level exists to answer questions; it should log them."</i>
/// Dwell per section is the readout for "is exploration fun", and without it the playtest's second
/// question can only be answered by asking people what they remember doing.
///
/// <para><b>Server only.</b> Every peer builds its own copy of the world (see
/// <c>Gameplay._Ready</c>'s "the world is static, not replicated" comment), so a client-side enter
/// log would multiply every crossing by the player count and would count the client's own
/// prediction rather than the authoritative position. The gate is the same one
/// <see cref="Sail.Game.Run.RespawnService"/> uses.</para>
///
/// <para><b>What it logs, and why not through Telemetry.</b> BT-0's packet asked for these to go
/// through <c>scripts/telemetry/Telemetry.cs</c> <i>if it exposes a fire-and-forget event call</i>.
/// It does not: <c>Telemetry</c> is a fixed-schema session summary (<c>BeginClientSession</c>, then
/// a hand-written set of counters — <c>NoteVoiceUsed</c>, <c>NotePropGrabbed</c>,
/// <c>NotePropThrown</c>, <c>NotePropDropped</c> — flushed once at quit). There is no
/// <c>Event(name, payload)</c> anywhere in it, and inventing one is explicitly out of this
/// packet's scope. So this prints one greppable line per crossing to the server log instead, in a
/// stable <c>key=value</c> shape a script can parse; whoever adds a real event API can redirect
/// these four calls and delete this comment.</para>
///
/// <para><b>Boundary discipline</b> (MECHANICS-BIBLE): sections do not overlap in plan (program §4;
/// the connector paths belong to the hub and lie in the gaps between footprints), so a player is in
/// at most one section at a time and enter/exit cannot interleave. A player standing exactly on a
/// footprint edge oscillates at most at the physics tick rate; that is accepted rather than
/// hysteresis-damped, because the consumer is a dwell histogram and not a state machine — nothing
/// downstream changes behaviour on a crossing.</para>
/// </summary>
public partial class SectionVolume : Area3D
{
    /// <summary>Which section this volume covers. Set in the scene file; the string is the
    /// <see cref="BubbleTestLayout.Section"/> name so the log key and the file name are the same
    /// token.</summary>
    [Export]
    public string SectionName { get; set; } = "";

    private bool _isServer;

    public override void _Ready()
    {
        // Instance is declared non-nullable but is genuinely absent in a bare scene-suite
        // instantiation (nothing ran Boot), so it is null-checked anyway; an unnetworked
        // instantiation counts as the server, which is what every other server-gated node here
        // does.
        _isServer = NetworkManager.Instance is null
                    || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        // Monitorable off: nothing ever queries THIS area, it only watches bodies. Leaving it on
        // costs a broadcast-phase entry per volume for no reader.
        Monitorable = false;
        Monitoring = _isServer;
        if (!_isServer) return;

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body) => Log(body, "enter");

    private void OnBodyExited(Node3D body) => Log(body, "exit");

    private void Log(Node3D body, string edge)
    {
        if (body is not SandboxAvatar avatar) return;
        GD.Print($"[bubbletest.section] section={SectionName} peer={avatar.OwnerPeerId} " +
                 $"edge={edge} phase={PhaseNow():F3}");
    }

    /// <summary>Where in the day/night cycle this crossing happened — the difference between
    /// "players explored the green section" and "players explored the green section only in
    /// daylight", which is the whole of program §6 item 8. Reads the shipped server-authoritative
    /// clock; −1 when no clock exists (a bare scene-suite instantiation) rather than a fake 0,
    /// which would be indistinguishable from midnight.</summary>
    private static float PhaseNow() =>
        MpFoundation.Game.World.CycleDriver.Instance is { } c ? c.Phase : -1f;
}
