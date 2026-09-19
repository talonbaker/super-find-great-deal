using Godot;

namespace MpFoundation.Ui;

/// <summary>Next-boot "Crash Report" prompt shown when the previous session crashed (spec
/// decision 6). Named to its report in-copy. Emits Decided(true/false); the autoload sends on
/// accept, discards on decline, and clears the pending crash either way.</summary>
public partial class CrashReportDialog : Control
{
    [Signal]
    public delegate void DecidedEventHandler(bool accepted);

    public override void _Ready()
    {
        // The one scrim. Its colour lives nowhere else — see UiThemeService.BindScrim.
        Design.UiThemeService.BindScrim(this);
        GetNode<Button>("Center/Panel/Buttons/SendButton").Pressed += () => Decide(true);
        var discard = GetNode<Button>("Center/Panel/Buttons/DiscardButton");
        discard.Pressed += () => Decide(false);
        // Pad/keyboard reachability: focus starts on the no-send default.
        discard.CallDeferred(Control.MethodName.GrabFocus);
        UiMotion.StaggerIn(GetNode<Control>("Center/Panel"));
    }

    private void Decide(bool accepted) => EmitSignal(SignalName.Decided, accepted);
}
