using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The clock on the wall</b> (CLOCK-1, 2026-09-19): the round's countdown as a thing in the
/// room, so a hider head-down in an aisle has a reason to look up.
///
/// <para><b>It is authored, not built</b> —
/// <c>scenes/game/world/supermarket/RoundClock.tscn</c> holds the panel and the two
/// <c>Label3D</c>s, and every room instances that one file. This script sets <b>text and
/// colour</b> and nothing else; it adds no node, and <c>SupermarketWorldSelfTest</c> counts the
/// rooms' packed nodes against their live ones so a clock that grew a child in <c>_Ready</c>
/// turns the suite red (<c>.claude/rules/godot-scenes.md</c>).</para>
///
/// <para><b>Nothing here holds its own appearance.</b> The panel's albedo and both labels'
/// colours come from <see cref="UiTokens"/> through <see cref="UiThemeService"/>, bound rather
/// than set once, so the clock and the HUD strip cannot disagree about what dark is. The scene
/// file deliberately carries no colour at all: a clock whose binding is lost renders white and
/// screams, which is the same choice the scrims made (<c>UiNoBespokeStylingTests</c>).</para>
///
/// <para><b>It reads; it is never pushed to.</b> No new wire, no RPC, no subscription to the
/// driver's events. <see cref="RoundAudio"/> polls <c>HideSeekDriver.Instance.View</c> at the
/// HUD's own 10 Hz and hands it to every registered clock, which is why there is no
/// <c>_Process</c> on this node: one poll for N clocks, exactly as one poll drives N HUD
/// widgets.</para>
///
/// <para><b>Hidden until <c>Synced</c>.</b> "Holding, round 1, nobody scoring" and "I have not
/// heard from the server yet" are the same bytes; a wall clock showing the first while it means
/// the second is worse than a blank wall, because a blank wall does not claim anything. A late
/// joiner's clock is therefore correct on the first frame it is visible at all — the smoke
/// asserts that against the server's own log rather than trusting it.</para>
/// </summary>
public partial class RoundClock : Node3D
{
    /// <summary>Node names inside <c>RoundClock.tscn</c>. Spelled once: a second spelling of a
    /// node is a silent miss, and this repo has paid for that already.</summary>
    public const string PanelNodeName = "Panel";

    /// <inheritdoc cref="PanelNodeName"/>
    public const string PhaseLabelNodeName = "PhaseLabel";

    /// <inheritdoc cref="PanelNodeName"/>
    public const string TimerLabelNodeName = "TimerLabel";

    /// <summary>Which room this clock is in, for the <c>--log-clock</c> line and for a capture's
    /// caption. Set per instance in the room scene; the keys are
    /// <c>SupermarketWorld.HoldingRoom</c> / <c>SearchRoom</c> / <c>TaskRoom</c>, so "which room"
    /// has one spelling across the level, the teleports and this log.</summary>
    [Export]
    public string Room { get; set; } = "";

    private MeshInstance3D? _panel;
    private Label3D? _phaseLabel;
    private Label3D? _timerLabel;
    private StandardMaterial3D? _panelMaterial;

    /// <summary>The authored pixel size of the second line, sampled once so
    /// <see cref="RoundClockLayout.FitPixelSize"/> has something to shrink FROM. Read off the
    /// scene rather than typed here — the size is a level-authoring decision and belongs in the
    /// file a level author opens.</summary>
    private float _timerBasePixelSize;

    /// <summary>What is currently painted, so a poll that changes nothing costs two string
    /// comparisons. The same early-out every HUD widget's <c>Tick</c> makes.</summary>
    private string _lastPhaseText = "";
    private string _lastTimerText = "";

    public override void _Ready()
    {
        _panel = GetNodeOrNull<MeshInstance3D>(PanelNodeName);
        _phaseLabel = GetNodeOrNull<Label3D>(PhaseLabelNodeName);
        _timerLabel = GetNodeOrNull<Label3D>(TimerLabelNodeName);

        if (_phaseLabel == null || _timerLabel == null || _panel == null)
        {
            // Loud, and then inert. A clock missing a label looks exactly like a clock the round
            // never reached, and the two have completely different fixes.
            GD.PushError($"[clock] RoundClock '{Name}' is missing {PanelNodeName}/"
                         + $"{PhaseLabelNodeName}/{TimerLabelNodeName} — check RoundClock.tscn.");
            return;
        }

        _timerBasePixelSize = _timerLabel.PixelSize;

        // The panel's material is LOCAL TO THE SCENE (see RoundClock.tscn) so three clocks are
        // three materials rather than one shared resource three nodes fight over. Duplicated
        // defensively anyway: a shared material here would mean the last clock to _Ready decides
        // the colour of all of them, which is invisible until somebody changes a token.
        _panelMaterial = _panel.GetActiveMaterial(0) as StandardMaterial3D;
        if (_panelMaterial != null && !_panelMaterial.ResourceLocalToScene)
        {
            _panelMaterial = (StandardMaterial3D)_panelMaterial.Duplicate();
            _panel.SetSurfaceOverrideMaterial(0, _panelMaterial);
        }

        UiThemeService.Bind(this, ApplyTokens);

        // A wall clock that shows a plausible default is worse than a blank wall — see the class
        // doc. RoundAudio turns it on when the round is synced and off again if it is not.
        Visible = false;

        RoundAudio.RegisterClock(this);
    }

    public override void _ExitTree() => RoundAudio.UnregisterClock(this);

    /// <summary>Repaints from the live token set. Called on bind and again on every temperature
    /// change, which is the difference between a clock that follows the light and one that was
    /// lit once at boot.</summary>
    private void ApplyTokens(UiTokens tokens)
    {
        if (_panelMaterial != null)
            _panelMaterial.AlbedoColor = tokens.SurfaceSunken;
        if (_phaseLabel != null)
            _phaseLabel.Modulate = tokens.InkRank3;
        if (_timerLabel != null)
            _timerLabel.Modulate = tokens.InkRank1;
    }

    /// <summary>
    /// Paint one poll's worth of round. Driven by <see cref="RoundAudio"/> at the HUD's cadence;
    /// there is no <c>_Process</c> on this node.
    /// </summary>
    /// <param name="view">The peer's own folded view — the same value the HUD strip reads, from
    /// the same message, which is what makes the two readouts agree by construction rather than
    /// by both being careful.</param>
    public void Apply(in HideSeekView view)
    {
        if (_phaseLabel == null || _timerLabel == null)
            return;

        Visible = true;

        string phaseText = HideSeekText.PhaseName(view.Phase);
        string timerText = HideSeekText.ClockLine(view.Phase, view.RemainingSec,
            view.TowersCompleted, view.LastTally);

        if (phaseText != _lastPhaseText)
        {
            _lastPhaseText = phaseText;
            _phaseLabel.Text = phaseText;
        }

        if (timerText == _lastTimerText)
            return;
        _lastTimerText = timerText;
        _timerLabel.Text = timerText;
        // Re-fit on every change rather than only on a phase change: the tally line and the timer
        // are different widths and both arrive as "the text changed".
        _timerLabel.PixelSize =
            RoundClockLayout.FitPixelSize(_timerBasePixelSize, timerText.Length);
    }

    /// <summary>Take the clock off the wall — no round to show. Not a fade: there is no sensible
    /// value to fade FROM, and a clock that lingers with the last round's number on it during a
    /// reconnect is telling the player something that is not true.</summary>
    public void Blank()
    {
        Visible = false;
        _lastPhaseText = "";
        _lastTimerText = "";
    }

    /// <summary>What this clock currently reads, for <c>--log-clock</c> and for the smoke. Read
    /// off the LABELS rather than recomputed from the view, so the line the suite compares
    /// against the server is the text a player would actually be looking at.</summary>
    public (string Phase, string Timer) CurrentText() => (_lastPhaseText, _lastTimerText);
}
