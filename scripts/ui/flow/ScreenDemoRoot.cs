using Godot;
using MpFoundation.Game.Presentation;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// The human-watchable scripted-playthrough demo (packet scope 2's dev entry): launch with
/// <c>--screen-demo</c> and step through every CORE-PROG-B1 screen in sequence —
/// RoundIntro → Day → Dusk → Night → verdict → RoundEnd → UpgradeLobby → next round → Loss
/// — with SPACE/ENTER/pad-A, against the <see cref="FakePlaythroughDriver"/> (no server,
/// no world, no Workstream A code). This is how the orchestrator demos progress to Talon
/// pre-integration; the headless twin (<c>--screenflow-selftest</c>) walks the SAME
/// <see cref="ScriptedPlaythrough.Steps"/> and asserts what this shows.
///
/// The Loss screen's buttons are live against the fake: PLAY AGAIN genuinely restarts the
/// script's world (the fake honours T10), LEAVE TO MENU is labeled but inert (no menu
/// under the demo, by design).
/// </summary>
public partial class ScreenDemoRoot : Node
{
    private FakePlaythroughDriver _driver = null!;
    private FlowScreens _screens = null!;
    private Label _stepReadout = null!;
    private int _nextStep;

    public override void _Ready()
    {
        _driver = new FakePlaythroughDriver();

        // A flat dark ground so the screens read without a world behind them.
        var backdrop = new CanvasLayer { Name = "Backdrop", Layer = 1 };
        var ground = new ColorRect { Color = Design.UiThemeService.Tokens.PageGround };
        ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(ground);
        AddChild(backdrop);

        _screens = FlowScreens.Attach(this, _driver, _driver, leaveToMenu: null, withToasts: true);
        FlowScreens.RecaptureMouseOnHide = false;

        // Operator chrome: which step is showing, and how to advance. Above everything,
        // including the loss scrim — it is rig furniture, not game UI.
        var chrome = new CanvasLayer { Name = "DemoChrome", Layer = 120 };
        _stepReadout = UiKit.Caption("");
        _stepReadout.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _stepReadout.OffsetTop = -48;
        _stepReadout.OffsetBottom = -16;
        chrome.AddChild(_stepReadout);
        AddChild(chrome);

        UpdateReadout("ready");
    }

    public override void _Process(double delta) => _driver.Tick(delta);

    public override void _UnhandledInput(InputEvent @event)
    {
        bool advance = @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter }
            or InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A };
        if (!advance)
            return;
        if (_nextStep >= ScriptedPlaythrough.Steps.Count)
        {
            UpdateReadout("script complete - PLAY AGAIN on the loss screen restarts it");
            return;
        }
        PlaythroughStep step = ScriptedPlaythrough.Steps[_nextStep++];
        step.Apply?.Invoke(_driver);
        _screens.ApplyCue(step.Cue);
        UpdateReadout(step.Name);
        // A restart via the loss screen's own button rewinds the script to the live round.
        if (_driver.PlayAgainRequests > 0 && _driver.State == PlaythroughState.RoundIntro)
            _nextStep = 3;
    }

    private void UpdateReadout(string stepName) =>
        _stepReadout.Text = $"SCREEN DEMO  |  step {_nextStep}/{ScriptedPlaythrough.Steps.Count}: {stepName}  |  SPACE / ENTER / (A) = next";
}
