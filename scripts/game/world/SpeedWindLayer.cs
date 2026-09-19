using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// The wind layer: the ear's speedometer, client-local, no wire. LD-4 (2026-09-02, D4-R1).
///
/// Two non-positional <see cref="AudioStreamPlayer"/>s — a pink body and a high hiss — on a bus
/// of their own under the Ambient group, with one <see cref="AudioEffectLowPassFilter"/> whose
/// cutoff and whose players' levels are driven every physics tick by
/// <see cref="SpeedWindStream"/> from the local avatar's horizontal speed.
///
/// <b>Voice accounting.</b> Two plain <c>AudioStreamPlayer</c>s, not <c>AudioStreamPlayer3D</c>s:
/// outside <see cref="AudioVoiceBudget"/>'s ≤24 3D ceiling entirely, by the budget's own doc —
/// the same standing as <c>AmbientBed</c>'s two loops and <see cref="SfxLab.PlayUi"/>. No
/// <see cref="SfxLab"/> loop slot is taken and <see cref="AudioVoiceBudget.Ceiling"/> does not
/// move. Both players run continuously at <see cref="SfxLab.SilentDb"/> when there is nothing to
/// say (the bed's precedent); the cost is two mixed voices, not two 3D voices.
///
/// <b>The bus.</b> <c>Ambient_Wind</c>, a third lane beside <c>Ambient_Bed</c> and
/// <c>Ambient_Scenery</c>: under the group so the World Ambience slider trims it and the
/// underwater filter on the group muffles it (you do not hear wind under water), but NOT the bed
/// lane, because the bed lane carries the voice duck and a speedometer that ducks when a teammate
/// talks is a speedometer that lies. This class creates the bus and owns its one effect —
/// declared here rather than in <see cref="AudioBuses"/>, the same small recorded fork
/// <c>SubmersionFilter</c> took, for the same reason (a shared-file edit on a wave this wide).
///
/// <b>What it deliberately is not.</b> Not positional (it is YOUR wind); not gusting (a pure
/// function of speed — the direction's constraint, THRILL-BIBLE §12); not modulated by anything
/// but speed (the honest lane, §8.3); not ducked on landing (the motor keeps horizontal speed
/// through a landing and so does this). Its silence when you stop is the most caused absence in
/// the game and is NOT §6.3's wrong silence.
///
/// <b>Lab attachment.</b> Sits in <c>scenes/dev/MovementPlayground.tscn</c> as a plain child. It
/// finds the avatar itself (the first <see cref="SandboxAvatar"/> with a follow camera, else the
/// first at all) because the playground builds its body in <c>_Ready</c>, after this node's own,
/// and because this node may not touch <c>MovementPlayground.cs</c>. <b>M</b> toggles the mute;
/// <c>--wind-off</c> after the <c>--</c> starts it muted. A one-line readout, bottom-right, shows
/// the state so a capture can prove it.
/// </summary>
public partial class SpeedWindLayer : Node
{
    /// <summary>The wind's own lane under the Ambient group.</summary>
    public const string Bus = "Ambient_Wind";

    /// <summary>The launch flag, after <c>--</c>, that starts the layer muted.</summary>
    public const string MuteFlag = "--wind-off";

    /// <summary>The toggle key. Free in the playground (it owns TAB, R, K, G, ESC and the panel's
    /// F-keys and arrows) and free in <c>SandboxCamera</c>.</summary>
    public const Key ToggleKey = Key.M;

    /// <summary>The A/B switch. Muted still steps the stream (so un-muting is instant and honest),
    /// it just suppresses the output — a fade of <see cref="SpeedWindStream.SuppressSeconds"/>,
    /// not a cut.</summary>
    [Export] public bool Muted { get; set; }

    /// <summary>Show the one-line state readout. On by default in the lab; a played level would
    /// turn it off.</summary>
    [Export] public bool ShowReadout { get; set; } = true;

    private static AudioEffectLowPassFilter? _lowPass;

    private readonly SpeedWindStream _stream = new();
    private AudioStreamPlayer? _body;
    private AudioStreamPlayer? _hiss;
    private SandboxAvatar? _avatar;
    private Label? _readout;
    private float _lastBodyDb = float.NaN;
    private float _lastHissDb = float.NaN;
    private float _lastCutoffHz = float.NaN;

    /// <summary>The live stream, for a self-test or a readout.</summary>
    public SpeedWindStream Stream => _stream;

    /// <summary>True once the players and the bus are up.</summary>
    public bool IsSounding => _body != null && _hiss != null;

    public override void _Ready()
    {
        // A PAUSABLE node does not run _PhysicsProcess while the tree is paused, so the
        // GetTree().Paused branch below would be DEAD CODE at the default Inherit: a menu
        // opening would FREEZE the wind at whatever level it was at rather than fading it out,
        // which is the opposite of "menu open → fades out". Always keeps the suppression slew
        // running through a pause. Nothing in this repo sets GetTree().Paused today (the
        // playground's ESC frees the mouse and does not pause), so this is unexercised in the
        // lab; it is here so the stated behaviour is true when a pause arrives. Precedent:
        // UiThemeService, Always for the same reason.
        ProcessMode = ProcessModeEnum.Always;

        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == MuteFlag)
                Muted = true;
        }

        EnsureBus();

        _body = new AudioStreamPlayer
        {
            Name = "Body",
            Stream = SfxLab.ToWavLooping(SpeedWindSynth.RenderBody()),
            Bus = Bus,
            VolumeDb = SfxLab.SilentDb,
        };
        _hiss = new AudioStreamPlayer
        {
            Name = "Hiss",
            Stream = SfxLab.ToWavLooping(SpeedWindSynth.RenderHiss()),
            Bus = Bus,
            VolumeDb = SfxLab.SilentDb,
        };
        AddChild(_body);
        AddChild(_hiss);
        _body.Play();
        _hiss.Play();

        if (ShowReadout)
            BuildReadout();
    }

    public override void _ExitTree()
    {
        // The bus and its filter are process-global and stay; hand the filter back open so a
        // scene that reuses the lane does not inherit a shut cutoff from the last frame here.
        if (_lowPass != null)
            _lowPass.CutoffHz = SpeedWindCurve.FullCutoffHz;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: ToggleKey })
        {
            Muted = !Muted;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null || _hiss == null)
            return;

        // A body freed under us (a scene swap, a rebuilt avatar) must not become a disposed-object
        // throw on the next tick; drop the handle and look again.
        if (_avatar != null && !IsInstanceValid(_avatar))
            _avatar = null;
        _avatar ??= FindLocalAvatar();

        bool suppressed = Muted || _avatar == null || GetTree().Paused;
        _stream.SetSuppressed(suppressed);
        _stream.Step(_avatar?.Velocity ?? Vector3.Zero, (float)delta);

        // Only write a value that changed: a rewrite of an identical cutoff is a coefficient
        // recompute for nothing, and the stream already bounds how far any one write moves.
        if (_stream.BodyDb != _lastBodyDb)
        {
            _body.VolumeDb = _stream.BodyDb;
            _lastBodyDb = _stream.BodyDb;
        }
        if (_stream.HissDb != _lastHissDb)
        {
            _hiss.VolumeDb = _stream.HissDb;
            _lastHissDb = _stream.HissDb;
        }
        if (_lowPass != null && _stream.CutoffHz != _lastCutoffHz)
        {
            _lowPass.CutoffHz = _stream.CutoffHz;
            _lastCutoffHz = _stream.CutoffHz;
        }

        if (_readout != null)
        {
            _readout.Text =
                $"wind {(Muted ? "MUTED" : "ON")}   speed {_stream.SpeedMps,5:F2} m/s   "
                + $"body {FormatDb(_stream.BodyDb)}   hiss {FormatDb(_stream.HissDb)}   "
                + $"cutoff {_stream.CutoffHz,5:F0} Hz   [M] toggle";
        }
    }

    /// <summary>The wind bus and its one filter, idempotent. Bus creation checks by name;
    /// the effect is placed only if no low-pass is already on the lane, and the handle is kept
    /// from construction rather than fetched back by index (sound-real-time-effects §3).</summary>
    public static void EnsureBus()
    {
        AudioBuses.EnsureLayout();
        int idx = AudioBuses.EnsureBus(Bus, AudioBuses.Ambient);
        int count = AudioServer.GetBusEffectCount(idx);
        for (int slot = 0; slot < count; slot++)
        {
            if (AudioServer.GetBusEffect(idx, slot) is AudioEffectLowPassFilter existing)
            {
                _lowPass = existing;
                return;
            }
        }
        var filter = new AudioEffectLowPassFilter
        {
            CutoffHz = SpeedWindCurve.WalkCutoffHz,
            Resonance = 0f, // a resonant peak swept across the spectrum whistles
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        };
        AudioServer.AddBusEffect(idx, filter, count);
        _lowPass = filter;
    }

    /// <summary>The live filter, or null before <see cref="EnsureBus"/>.</summary>
    public static AudioEffectLowPassFilter? LowPass => _lowPass;

    /// <summary>The body whose speed is the wind's. Prefers the avatar a camera is registered
    /// against (the local one in any session shape); falls back to the first avatar in the tree,
    /// which in the offline playground is the only one.</summary>
    private SandboxAvatar? FindLocalAvatar()
    {
        SandboxAvatar? first = null;
        // FindChildren's type filter matches NATIVE class names, not C# script classes — asking
        // for "SandboxAvatar" returns nothing (the first headed run proved it: speed 0.00 with the
        // body at 6.08). Filter on the native base and narrow with `is`.
        foreach (Node n in GetTree().Root.FindChildren("*", nameof(CharacterBody3D), recursive: true, owned: false))
        {
            if (n is not SandboxAvatar a)
                continue;
            if (a.HasFollowCamera)
                return a;
            first ??= a;
        }
        return first;
    }

    private void BuildReadout()
    {
        var layer = new CanvasLayer { Name = "WindReadout", Layer = 5 };
        _readout = new Label
        {
            Name = "Text",
            Text = "wind …",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _readout.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.86f));
        _readout.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _readout.AddThemeConstantOverride("outline_size", 4);
        _readout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        _readout.GrowHorizontal = Control.GrowDirection.Begin;
        _readout.GrowVertical = Control.GrowDirection.Begin;
        _readout.OffsetRight = -12f;
        _readout.OffsetBottom = -10f;
        layer.AddChild(_readout);
        AddChild(layer);
    }

    private static string FormatDb(float db) =>
        db <= SfxLab.SilentDb ? "  off  " : $"{db,5:F1} dB";
}
