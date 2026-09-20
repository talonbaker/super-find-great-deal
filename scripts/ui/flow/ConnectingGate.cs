using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet surface 6 (spec §3.5 row 6) — the flow-level "Connecting..." gate: shown while
/// the playthrough view is unsynced or the machine is in Boot, so a joiner never stares at
/// a live world that hasn't told them what state it is in. Complements (does not replace)
/// the world-build <see cref="LoadingHintOverlay"/>: that one dismisses on the clock sync
/// at layer 100 above this; this one dismisses on the FLOW sync — first render gated on
/// synced state, the established idiom.
/// </summary>
public partial class ConnectingGate : FlowScreenBase
{
    protected override ScreenId Id => ScreenId.Connecting;

    public ConnectingGate(IPlaythroughView view) : base(view) { }

    protected override void BuildContent()
    {
        Column.AddChild(UiKit.Heading(HeadingText));
        Column.AddChild(UiKit.Body(BodyText));
    }

    protected override void OnShow() { }

    // --- pure display decisions ------------------------------------------------------------

    public const string HeadingText = "CONNECTING";
    public const string BodyText = "Waiting for the camp...";
}
