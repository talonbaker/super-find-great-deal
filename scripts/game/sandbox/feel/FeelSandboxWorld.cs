using System.Globalization;
using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// The interaction/feel sandbox: the SHIPPED avatar and the SHIPPED camera, driving the ported
/// interaction system instead of the game's own <see cref="CarryController"/>.
///
/// -----------------------------------------------------------------------------------------------
/// WHY THIS SCENE EXISTS
/// -----------------------------------------------------------------------------------------------
/// The system in <c>scripts/game/sandbox/feel/</c> was built and tuned in shader-lab, on a capsule,
/// under an ORBIT camera -- left-drag to swing the view, with the character's facing a separate
/// thing that never turned with it. Talon's verdict on 2026-09-05, and it is a verdict about the
/// rig rather than about the system: <i>"nothing about it is the way it would actually be working
/// in a game... the mouse isn't connected to the camera. The player character is a pill that does
/// not turn when the camera is moved."</i>
///
/// He is right, and the consequence is sharper than a comfort complaint: under that rig you cannot
/// PERFORM the action the system exists to be judged on. Targeting is scored off the view axis
/// (<see cref="Interactor.AimHalfAngleDeg"/>) and the hold anchor is built in the carrier's frame,
/// so a view that turns without the body is measuring one thing and carrying in another. Every
/// reaction it produced was contaminated by the harness.
///
/// So the port went this way round. The character could not affordably come to the lab -- the
/// avatar stack is roughly 12k lines across four namespaces -- but the interaction system is four
/// files that import nothing but Godot, and it came here in an afternoon.
///
/// -----------------------------------------------------------------------------------------------
/// THE SHIPPED CARRY IS NOT TOUCHED, AND THAT IS THE POINT OF USING InteractOverride
/// -----------------------------------------------------------------------------------------------
/// <see cref="SandboxAvatar.InteractOverride"/> already exists for exactly this: a hook that gets
/// first refusal on the interact key, documented as "one E, one affordance grammar,
/// interactive-first priority". This class points it at the interactor and ALWAYS RETURNS TRUE, so
/// the key never falls through to <c>CarryController</c>. That controller is still there, still
/// compiled, still covered by Run-CarryTest, and in this scene it is simply never asked anything.
/// Nothing about the shipping carry path is at risk from this branch.
///
/// The throw key is read directly here rather than through a second override, because
/// <c>HandleCarryIntent</c>'s offline branch routes Throw into <c>Carry.Throw</c>, and this scene's
/// CarryController is permanently empty -- so that call is a no-op and there is nothing to
/// suppress. One override, not two, for one behaviour that needed overriding.
/// </summary>
public partial class FeelSandboxWorld : Node3D
{
    private const string HoverShader = "res://resources/shaders/feel/hover_outline.gdshader";

    private SandboxAvatar _player = null!;
    private SandboxCamera _camera = null!;
    private Interactor _hand = null!;
    private FeelAudio _audio = null!;
    private ShaderMaterial _hoverMat = null!;
    private Node3D _stageRoot = null!;
    private FeelStage.Built _stage = null!;

    private Label _readout = null!;

    // --------------------------------------------------------------------------- the shot flag
    //
    // Ported from the lab's --shot, and it earns its keep for the same reason it did there: nobody
    // can look at this scene except by playing it, so without a capture path the only way to check
    // that the stage landed where it was meant to is to ask Talon to look. That is the wrong person
    // to spend on a layout typo.
    //
    //   ... res://scenes/game/sandbox/FeelSandbox.tscn -- --feel-shot C:/tmp/feel.png[,seconds]
    //
    // Headed only: ViewportCapture refuses headless outright rather than writing a plausible file
    // with no image in it.
    private string? _shotPath;
    private double _shotAtSec = 2.0;
    private double _elapsed;
    private bool _shotFired;

    public override void _Ready()
    {
        _camera = GetNode<SandboxCamera>("Camera");
        _player = GetNode<SandboxAvatar>("Player");

        // Identical wiring to SandboxWorld: same intent source, same attach, same aim camera.
        // Anything different here would be a difference between this test and the game, which is
        // the one thing this scene exists not to have.
        _player.IntentSource = new LocalInputIntentSource(_camera);
        _camera.Attach(_player);
        _player.AimCamera = _camera.CameraNode;

        BuildStage();
        BuildHand();
        BuildReadout();
        ParseShotArgs();
    }

    private void ParseShotArgs()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--feel-shot" || i + 1 >= args.Length) continue;

            string[] parts = args[i + 1].Split(',');
            _shotPath = parts[0];
            if (parts.Length > 1
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double at)
                && double.IsFinite(at) && at >= 0)
            {
                _shotAtSec = at;
            }
            _readout.Visible = false;
            return;
        }
    }

    private async void TakeShot(string path)
    {
        await Dev.ViewportCapture.SaveAsync(this, path, "feel-shot");
        GetTree().Quit();
    }

    private void BuildStage()
    {
        _hoverMat = new ShaderMaterial { Shader = GD.Load<Shader>(HoverShader) };
        _stageRoot = new Node3D { Name = "Stage" };
        AddChild(_stageRoot);
        _stage = FeelStage.Build(_stageRoot, _hoverMat);
    }

    private void BuildHand()
    {
        _audio = new FeelAudio { Name = "FeelAudio" };
        AddChild(_audio);

        // A CHILD OF THE AVATAR, so the reach volume and the hold anchor both follow the body
        // without anything copying a transform each frame. The interactor builds its own Area3D in
        // _Ready, and AddChild on a node already in the tree runs that synchronously -- so the hand
        // is live by the time this method returns.
        _hand = new Interactor
        {
            Name = "Hand",
            Carrier = _player,
            Camera = _camera.CameraNode,
            CarrierVisual = _player.Visual,
            Audio = _audio,
        };
        _player.AddChild(_hand);

        // ALWAYS TRUE: see the class header. Returning false when there is nothing to grab would
        // hand the press to the shipped CarryController, and one key would drive two carry systems.
        _player.InteractOverride = () =>
        {
            if (!_hand.TryGrab()) _hand.TryRelease();
            return true;
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        // Read through the same actions LocalInputIntentSource uses rather than raw keys, so a
        // controller works here exactly as it does in the game.
        if (Input.IsActionJustPressed("throw")) _hand.TryThrow();

        UpdateReadout();

        if (_shotPath == null || _shotFired) return;
        _elapsed += delta;
        if (_elapsed < _shotAtSec) return;
        _shotFired = true;
        TakeShot(_shotPath);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            // T, not Q. The lab used Q for this, but Q is THROW in the game and a test rig that
            // rebinds a shipped verb is a test rig that is no longer testing the game.
            case Key.T:
                Interactable? it = _hand.Carried ?? _hand.Target;
                if (it != null)
                {
                    it.Release = it.Release == Interactable.ReleaseMode.Tumble
                        ? Interactable.ReleaseMode.Snap
                        : Interactable.ReleaseMode.Tumble;
                }
                break;

            case Key.R:
                ResetStage();
                break;
        }
    }

    /// <summary>Put every prop back where it started. A full rebuild rather than a per-prop
    /// transform restore: the props are built in code from one description, so re-running that
    /// description is the only reset that cannot drift out of step with it.</summary>
    private void ResetStage()
    {
        _hand.TryRelease();
        _hand.ForgetAll();

        _stageRoot.QueueFree();
        BuildStage();
    }

    private void BuildReadout()
    {
        var layer = new CanvasLayer { Name = "Readout" };
        AddChild(layer);

        _readout = new Label
        {
            Name = "Text",
            Position = new Vector2(16, 16),
            Modulate = new Color(1, 1, 1, 0.9f),
        };
        _readout.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _readout.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_readout);
    }

    private void UpdateReadout()
    {
        Interactable? target = _hand.Target;
        Interactable? held = _hand.Carried;
        Interactable? subject = held ?? target;

        string mode = subject != null ? subject.Release.ToString().ToUpperInvariant() : "-";
        string heft = subject != null ? $"{_hand.HeftOf(subject):0.00}" : "-";

        _readout.Text =
            $"WASD move  ·  mouse look  ·  Space jump  ·  E grab/place  ·  Q throw  ·  T flip mode  ·  R reset\n" +
            $"looking at : {(target != null ? target.Label : "-")}\n" +
            $"holding    : {(held != null ? held.Label : "-")}   lag {_hand.CarryLag:0.00} m\n" +
            $"mode       : {mode}   heft {heft}   in reach {_hand.NearCount}\n" +
            $"last       : {_hand.LastVerdict}";
    }
}
