using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The opening beat, first of two: the studio mark (<see cref="Branding.Studio"/> — the name
/// since 2026-09-02; a studio logo IMAGE would still replace the Label outright, per the
/// 2026-08-15 note)
/// over a loading bar tied to REAL startup work. BootWarmup bakes every procedural
/// sound, both score beds, and a warm-up draw of each material/shader configuration
/// while the bar fills; the bar reports steps actually completed, never a timer.
/// Skippable once shown (a keypress abandons the remainder harmlessly — the game just
/// falls back to first-use costs). Hands off to the TitleScreen ("press any key"),
/// which owns the game's wordmark; this beat is the studio's.
///
/// Look per the Art Bible: matte near-black, one warm pastel accent, soft ease-outs,
/// nothing overshooting, nothing flashing.
/// </summary>
public partial class Splash : Control
{
    private const double MinSplashSec = 1.6;

    private bool _advanced;
    private bool _warmupDone;
    private double _shownSec;
    private ProgressBar _bar = null!;
    private Label _step = null!;

    public override void _Ready()
    {
        var title = GetNode<Label>("Center/Column/Title");
        var line = GetNode<ColorRect>("Center/Column/LoadingLine");
        var column = GetNode<VBoxContainer>("Center/Column");

        // The studio mark slot. Talon named the studio on 2026-09-02 (addendum §10: "change to
        // show the studio name, Great-Grand-Software"), so the placeholder that deliberately was
        // NOT a name is now the name. Read from Branding, never typed here: the splash and the
        // title screen are the two surfaces the branding constants exist to keep in step, and a
        // literal in this method is the second copy that goes stale. The 2026-08-15 note still
        // stands for the IMAGE — when a studio logo lands it replaces this Label outright.
        title.Text = Branding.Studio;
        title.ThemeTypeVariation = "Hero";

        // The loading bar: a slim sunken track that fills with ember. This is the one place a
        // continuous bar is allowed — the register's no-meters rule is about the round, and this
        // is the frame around it. Inside the round, progress is worded stages.
        Design.UiTokens tokens = Design.UiThemeService.Tokens;
        var track = new StyleBoxFlat { BgColor = tokens.SurfaceSunken };
        track.SetCornerRadiusAll(Design.UiScale.RadiusTight);
        var fill = new StyleBoxFlat { BgColor = tokens.Accent };
        fill.SetCornerRadiusAll(Design.UiScale.RadiusTight);
        _bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(240, 6),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        _bar.AddThemeStyleboxOverride("background", track);
        _bar.AddThemeStyleboxOverride("fill", fill);
        column.AddChild(_bar);

        _step = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _step.ThemeTypeVariation = "Caption";
        column.AddChild(_step);

        // The accent line beneath the wordmark: the splash's single ember instance. Its
        // colour was a lavender literal in the scene file, left over from the retired
        // palette; it lives here now and nowhere else.
        line.CustomMinimumSize = new Vector2(48, Design.UiScale.BorderWeight);
        Design.UiThemeService.Bind(line, t => line.Color = t.Accent);

        // Intro: fade + settle, nothing overshoots.
        title.Modulate = new Color(title.Modulate, 0f);
        title.Scale = new Vector2(0.98f, 0.98f);
        Tween intro = CreateTween().SetParallel(true);
        intro.TweenProperty(title, "modulate:a", 1f, 0.9)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        intro.TweenProperty(title, "scale", Vector2.One, 0.9)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        // The real work. Its progress IS the bar.
        var warmup = new BootWarmup { Name = "Warmup" };
        warmup.Progress += (fraction, label) =>
        {
            _bar.Value = fraction;
            _step.Text = label;
        };
        AddChild(warmup);
        RunWarmup(warmup);
    }

    private async void RunWarmup(BootWarmup warmup)
    {
        try
        {
            await warmup.RunAsync();
        }
        catch (System.Exception e)
        {
            // Warm-up is best-effort: a failure means first-use costs return, nothing
            // more. It must never soft-lock the splash.
            GD.PushWarning($"boot warm-up aborted: {e.Message}");
        }
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        _warmupDone = true;
        _step.Text = "";
        _bar.Value = 1;
    }

    public override void _Process(double delta)
    {
        _shownSec += delta;
        if (_warmupDone && _shownSec >= MinSplashSec)
            Advance();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false }
            or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true })
            Advance();
    }

    private void Advance()
    {
        if (_advanced || !GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        _advanced = true;

        // Cross-fade out, then swap to the menu.
        Tween outro = CreateTween();
        outro.TweenProperty(this, "modulate:a", 0f, 0.25).SetTrans(Tween.TransitionType.Sine);
        outro.TweenCallback(Callable.From(() =>
        {
            if (IsInsideTree())
                GetTree().ChangeSceneToFile(ScenePaths.Title);
        }));
    }
}
