using Godot;

namespace MpFoundation.Ui;

public partial class SettingsMenu : Control
{
    public override void _Ready()
    {
        // Standalone screen: the top-bar '‹ Back' owns navigation (the panel's own
        // Back button is hidden here — it stays for the pause-overlay embedding,
        // whose BackRequested wiring is preserved below for safety).
        GetNode<Button>("TopBar/BackButton").Pressed +=
            () => GetTree().ChangeSceneToFile(ScenePaths.MainMenu);
        GetNode<SettingsPanel>("Center/SettingsPanel").BackRequested +=
            () => GetTree().ChangeSceneToFile(ScenePaths.MainMenu);

        UiMotion.StaggerIn(GetNode<Control>("Center/SettingsPanel"));
    }
}
