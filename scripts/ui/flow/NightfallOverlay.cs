using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// Packet 3b's DuskToNight treatment — the brief moment that SPELLS OUT what changes mechanically
/// at night (the brief's hard requirement: told, not discovered blind). Triggered by
/// <see cref="PhaseToastLayer"/> on the real crossing (extend, never a parallel system — the toast
/// layer stays the one PhaseCrossed subscriber) and by the scripted demo's cue. Content comes from
/// <see cref="PhaseToastText"/>'s pure statics, same seam as the toasts themselves.
///
/// Auto-dismisses after a short hold: it is a moment, not a modal — night play continues under it,
/// so it never takes input, never suppresses world UI, never blocks.
///
/// <para><b>It used to be full-screen, and that was a law violation, not a taste call</b> (Talon's
/// 2026-08-14 playtest, logged as a repeat offence). This treatment plays while the player still
/// has the camera and still has movement, and the standing law is that non-diegetic UI may cover
/// the screen only if camera AND controls were taken with it — see
/// <see cref="Design.UiCoverageLaw"/>. The old build painted a full-rect wash at 55% alpha over
/// the whole frame: the player kept walking into the dark reading a paragraph laid across the dark
/// they were walking into.</para>
///
/// <para><b>What replaced it, and why the words survived.</b> The fix is the plate, not the beat:
/// the same heading, the same keyline, the same lines (four in a world that runs the scored
/// playthrough, none in one that does not - see <see cref="PhaseToastText.NightfallLinesFor"/>),
/// on a bounded card hanging from
/// an anchor just below the middle of the frame (<see cref="Design.UiColumns.LowerBandTopAnchor"/>)
/// — so it cannot cross the aim point at any frame height, and the horizon the player is actually
/// watching stays uncovered. It measures around a sixth of the frame against a quarter-frame
/// bound. Making the transition genuinely take control instead would satisfy the same law from the
/// other side, but that is a design change to the beat and it is not this packet's to make.</para>
/// </summary>
public partial class NightfallOverlay : CanvasLayer
{
    private const double FadeInSec = 0.4;
    private const double HoldSec = 5.0;
    private const double FadeOutSec = 0.8;

    private Control _root = null!;
    private bool _playing;

    /// <summary>True while the treatment is on screen — the self-test's probe.</summary>
    public bool Playing => _playing;

    /// <summary>The bounded plate the words sit on — the self-test measures THIS rather than
    /// guessing at the layer's extent, so the coverage assertion reads the thing that paints.</summary>
    public Control Plate { get; private set; } = null!;

    public override void _Ready()
    {
        Layer = ScreenRouter.NightfallLayer;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // Centred horizontally, hanging from an anchor just below the middle of the frame, grown
        // from its own content in both directions. Anchored by FRACTION rather than lifted by
        // pixels because the thing it has to miss — the aim point — is itself a fraction of the
        // frame: see Design.UiColumns.LowerBandTopAnchor. No wash, no scrim, nothing behind the
        // plate; the dark arriving in the world is the atmosphere here and does not need help
        // from a rectangle.
        PanelContainer plate = UiKit.Panel(out VBoxContainer column);
        // Nothing this treatment draws may swallow a pointer event. That is not tidiness: under
        // Design.UiCoverageLaw, a surface that accepts input is a surface that has TAKEN the
        // player, and the law then allows it the whole frame. This beat has not taken anything,
        // and the self-test reads these filters to decide which branch of the law applies.
        plate.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.MouseFilter = Control.MouseFilterEnum.Ignore;
        plate.AnchorLeft = 0.5f;
        plate.AnchorRight = 0.5f;
        plate.AnchorTop = Design.UiColumns.LowerBandTopAnchor;
        plate.AnchorBottom = Design.UiColumns.LowerBandTopAnchor;
        plate.OffsetLeft = 0f;
        plate.OffsetRight = 0f;
        plate.OffsetTop = 0f;
        plate.OffsetBottom = 0f;
        plate.GrowHorizontal = Control.GrowDirection.Both;
        plate.GrowVertical = Control.GrowDirection.End;
        _root.AddChild(plate);
        Plate = plate;

        column.AddChild(Centred(UiKit.Heading(PhaseToastText.NightfallHeading)));

        // The screen's one accent instance: the keyline under NIGHT. It was the ember until UI-3
        // (2026-08-29) took warm hues off the interface; UiKit.Keyline is theme-driven, so it
        // followed the token rename without needing an edit here.
        ColorRect keyline = UiKit.Keyline();
        keyline.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        column.AddChild(keyline);

        // Read through NightfallLinesFor, never the bare array: those lines name creatures, fires
        // to feed, freezing and the winter cache, none of which exist in a world that runs no
        // scored playthrough. Talon hit exactly that in the bubble test on 2026-08-29 (note 4) --
        // LOSS-1 removed the loss screen while this plate went on saying the same four things,
        // larger, at every nightfall. See PhaseToastText.NightfallLinesFor for why the gate is a
        // method rather than baked into the array (FlowCopyTests reads the array directly, and
        // the NetworkManager autoload makes the world id never null and silently "bubbletest").
        //
        // Safe to read the world here rather than take a parameter: this overlay has exactly one
        // construction site (PhaseToastLayer), and Run-HudLayoutTest.ps1 already launches
        // --world camp -- its own comment records going red when LAUNCH-1 moved the default --
        // so HudLayoutSelfTest's coverage-law plate keeps its lines and stays measurable.
        foreach (string line in PhaseToastText.NightfallLinesFor(Sail.Game.Run.WorldRunFlow.Current))
            column.AddChild(Centred(UiKit.Body(line)));
    }

    /// <summary>The column fills the plate, so centring is the label's own alignment rather than a
    /// CenterContainer — one node fewer per line, and the plate keeps sizing to its longest line.</summary>
    private static Label Centred(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>Plays the treatment once; a re-trigger while playing is ignored (two
    /// crossings this close only happen under test-speed clocks, and a garbled overlap
    /// would be worse than a missed repeat — the PhaseToastLayer queue reasoning).</summary>
    public async void ShowNightfall()
    {
        if (_playing)
            return;
        _playing = true;

        Tween inTween = CreateTween();
        inTween.TweenProperty(_root, "modulate:a", 1f, FadeInSec).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(inTween, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(this))
            return;
        await ToSignal(GetTree().CreateTimer(HoldSec), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(this))
            return;
        Tween outTween = CreateTween();
        outTween.TweenProperty(_root, "modulate:a", 0f, FadeOutSec).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(outTween, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(this))
            return;
        _playing = false;
    }
}
