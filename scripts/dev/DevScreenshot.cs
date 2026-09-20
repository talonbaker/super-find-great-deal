using System;
using System.IO;
using Godot;

namespace MpFoundation.Dev;

/// <summary>
/// A screenshot key for a HUMAN. F9 saves the current frame, wherever the player happens to be —
/// the menu, the camp, a dev lab, mid-sneak-out at night.
///
/// <para>Until this existed, every capture in the project was bot-only: three launch flags
/// (<c>--capture-dir</c>, <c>--capture-at</c>, <c>--capture-cam</c>) driving BotHarness on a
/// scripted timeline. Excellent for a CI gate that must shoot the same frame every run, useless
/// during a live session, where the frame worth keeping is the one nobody scheduled. Same
/// underlying path though — see <see cref="ViewportCapture"/>, which both callers share.</para>
///
/// <para><b>Autoload, on purpose.</b> A screenshot key that only works in the scenes someone
/// remembered to wire it into is a key you find out was missing after the session. Registered in
/// project.godot's [autoload] block, so it is present in every scene there will ever be, and it
/// disables itself on a headless peer (a dedicated server has no frame to save).</para>
///
/// <para>Shots land in <c>captures/playtest/</c> under the repo, which is GITIGNORED — correct
/// for throwaway session output, and the reason the repo convention is to copy the few keepers
/// into <c>docs/superpowers/status/&lt;packet&gt;/</c> when they need to back a claim. Override
/// the directory with <c>SAIL_SHOT_DIR</c>, same idiom as SAIL_FACELAB_OUT.</para>
/// </summary>
public partial class DevScreenshot : CanvasLayer
{
    /// <summary>F9. Not F12 (Steam's overlay eats it before the game sees it) and not F3
    /// (PerfHud's toggle).</summary>
    private const Key ShotKey = Key.F9;

    private const double ToastSec = 2.5;

    private string _dir = "";
    private Label _toast = null!;
    private double _toastLeft;
    private int _shotCount;
    private bool _busy;

    public override void _Ready()
    {
        // Above every HUD layer except LoadingHintOverlay's 100 — the confirmation has to be
        // readable over whatever was on screen, but it must never cover the loading overlay.
        Layer = Ui.Design.UiLayers.DevScreenshot;

        if (DisplayServer.GetName() == "headless")
        {
            // Dedicated servers, bots and every headless self-test load the autoloads too.
            // Nothing to photograph and no keyboard: go completely inert rather than sit in
            // the input path of every CI process.
            SetProcessUnhandledKeyInput(false);
            SetProcess(false);
            return;
        }

        _dir = OS.GetEnvironment("SAIL_SHOT_DIR");
        if (string.IsNullOrEmpty(_dir))
        {
            // In an exported build res:// is inside the read-only .pck, so the repo path does
            // not exist to write to; user:// is the only writable location there.
            _dir = ProjectSettings.GlobalizePath(
                OS.HasFeature("template") ? "user://screenshots" : "res://captures/playtest");
        }

        _toast = new Label
        {
            Position = new Vector2(16, 16),
            Visible = false,
            LabelSettings = new LabelSettings
            {
                FontSize = 15,
                FontColor = new Color(0.85f, 1f, 0.88f),
                OutlineSize = 4,
                OutlineColor = new Color(0f, 0f, 0f, 0.85f),
            },
        };
        AddChild(_toast);
        SetProcess(false); // nothing to tick until the first shot puts a toast up
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: ShotKey })
            return;
        GetViewport().SetInputAsHandled();
        // A held key repeat or a second press mid-write would otherwise interleave two awaits on
        // the same frame signal and produce one file twice.
        if (_busy)
            return;
        _busy = true;
        _ = ShootAsync();
    }

    private async System.Threading.Tasks.Task ShootAsync()
    {
        try
        {
            // Timestamped, plus a per-session counter so two shots inside the same second cannot
            // overwrite each other — the exact case a burst of presses during one beat produces.
            string stem = $"shot-{DateTime.Now:yyyyMMdd-HHmmss}-{++_shotCount:D3}";
            string path = Path.Combine(_dir, $"{stem}.png");

            // Toast AFTER the save, never before: it is drawn into the same viewport the capture
            // reads, so announcing the shot first would put the announcement in the shot.
            bool ok = await ViewportCapture.SaveAsync(this, path, "shot");
            _toast.Text = ok ? $"saved {path}" : $"screenshot FAILED -> {path}  (see console)";
            _toast.Visible = true;
            _toastLeft = ToastSec;
            SetProcess(true);
        }
        finally
        {
            _busy = false;
        }
    }

    public override void _Process(double delta)
    {
        _toastLeft -= delta;
        if (_toastLeft > 0)
            return;
        _toast.Visible = false;
        SetProcess(false);
    }
}
