using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The playtest foreword: shown once from MainMenu before a fresh tester ever clicks Host/Join,
/// telling them what this build is for and what feedback we actually need. Same CanvasLayer
/// modal pattern as <see cref="HowToPlayPanel"/> (scrim, centered panel, X + ESC to close,
/// persisted "don't show again") so the two doors feel like one visual language, not two
/// competing popup systems.
/// </summary>
public partial class PlaytestForewordPanel : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();

    public override void _Ready()
    {
        // The one scrim. Its colour lives nowhere else — see UiThemeService.BindScrim.
        Design.UiThemeService.BindScrim(this);
        Layer = Design.UiLayers.PauseChildPanel;
        GetNode<Button>("Center/Panel/Header/CloseButton").Pressed += Close;
        var toggle = GetNode<PillToggle>("Center/Panel/DontShowRow/DontShowToggle");
        toggle.ButtonPressed = OnboardingSettings.PlaytestForewordDismissed;
        toggle.Toggled += OnboardingSettings.SetPlaytestForewordDismissed;
        UiMotion.StaggerIn(GetNode<Control>("Center/Panel"));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("pause") || @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close() => EmitSignal(SignalName.Closed);
}
