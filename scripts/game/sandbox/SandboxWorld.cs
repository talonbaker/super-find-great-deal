using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Offline mechanics sandbox: one player avatar, three wandering dummies, and grabbable
/// crate/ball props, on a flat pastel testbed. Fully authored in Sandbox.tscn — terrain,
/// props, and avatars are all real nodes placed and moveable in the Godot editor viewport
/// (see Playground/PlaygroundWorld for the same pattern). This class only wires behaviour
/// onto what the scene already contains; it builds nothing. No server, no matchmaking, no
/// network peer — launch it directly:
///   Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/game/sandbox/Sandbox.tscn
/// </summary>
public partial class SandboxWorld : Node3D
{
    private SandboxAvatar _player = null!;
    private readonly List<Node3D> _avatars = new();
    private InteractHighlighter _highlighter = null!;

    public override void _Ready()
    {
        var camera = GetNode<SandboxCamera>("Camera");

        _player = GetNode<SandboxAvatar>("Player");
        _player.IntentSource = new LocalInputIntentSource(camera);
        camera.Attach(_player);
        _player.AimCamera = camera.CameraNode; // real play: E acts on the looked-at thing
        _avatars.Add(_player);

        Ui.InteractPrompt.Attach(this);
        _highlighter = new InteractHighlighter(() => _player);

        WireDummy(GetNode<SandboxAvatar>("DummyA"));
        WireDummy(GetNode<SandboxAvatar>("DummyB"));
        WireDummy(GetNode<SandboxAvatar>("DummyC"));
    }

    private void WireDummy(SandboxAvatar dummy)
    {
        dummy.IntentSource = new WanderIntentSource(dummy);
        _avatars.Add(dummy);
    }

    // Pickup-candidate highlight + interact chip, throttled — readability juice, not gameplay.
    // Shared with the networked path (see InteractHighlighter), which is the whole point: this
    // poll used to exist only here, so the shipping multiplayer world had no affordance at all.
    public override void _PhysicsProcess(double delta) => _highlighter.Tick(delta);
}
