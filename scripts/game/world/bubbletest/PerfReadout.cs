using Godot;
using MpFoundation.Game.World;
using MpFoundation.Ui.Design;
using Sail.Game.Bubble;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// A once-a-second console readout of what this frame actually cost and what the bubble film
/// actually received. Off unless <c>--bt-perf-readout</c> is passed; adds nothing to a normal
/// session but one flag test in <c>_Ready</c>.
///
/// <para><b>Why it exists (DARK-1, 2026-08-28).</b> This packet turns on the first real-time
/// sun shadow in this repo, which reverses PR #55's ruling — and PR #55's ruling came from a
/// draw-call measurement, so reversing it on an assurance rather than on a measurement would be
/// strictly worse than leaving it alone. `.claude/rules/single-writer.md` still records the
/// convention as closed; the numbers this prints are what the reversal has to be argued on, and
/// the decision itself is Talon's.</para>
///
/// <para><b>Why it also prints the film's emission energy.</b> Corroboration, not proof. The
/// render is the only witness to whether the bubbles glow — every read-the-value-back API in
/// Godot is editor-only and lies in a running project (see <see cref="NightAidDriver"/>'s note,
/// which cost FIX-1 two capture rounds). What this line CAN close is the other half: whether the
/// driver computed a non-zero energy at all at the moment a capture was taken. A dark capture
/// plus a non-zero energy here means the write is landing and the look needs tuning; a dark
/// capture plus a zero means the clock never reached night. Those are different bugs and this is
/// the cheapest thing that separates them.</para>
///
/// <para><b>Frame time is `TimeProcess`, not 1/fps.</b> fps is capped by vsync on a headed run,
/// so it reports the monitor rather than the renderer and would show no difference at all
/// between a scene with shadows and one without until the GPU was already over budget.</para>
///
/// <para><b>LD-2 (2026-09-02): two cadence lines, on screen.</b> Under the same flag this now
/// also draws <c>since-act  N.N s</c> and <c>stop  N.N s/min</c> as a screen-space label
/// (<see cref="UiLayers.PerfHud"/>, top-left, the playground readout's style — never a
/// billboard in the world, per Talon's ruling), refreshed every frame off
/// <see cref="CadenceTracker"/>'s clock, and appends the same numbers to the once-a-second
/// console line so a capture bot's log carries them too. The label is what a headed capture
/// can photograph; the console line is what a script can grep. Before this packet the class
/// was console-only, which is why the label is new rather than "another line".</para>
/// </summary>
public partial class PerfReadout : Node
{
    /// <summary>Flag that turns this on. Read from <c>GetCmdlineUserArgs</c>, i.e. it must sit
    /// AFTER the <c>--</c> separator — a flag before it is swallowed by the engine and looks
    /// exactly like a hang.</summary>
    public const string Flag = "--bt-perf-readout";

    /// <summary>Node name, so a harness can find it.</summary>
    public const string NodeName = "PerfReadout";

    private const double IntervalSec = 1.0;

    private bool _on;
    private double _timer;

    // Accumulated across the interval rather than sampled on one frame: a single frame's draw
    // call count lands wherever culling happened to leave it, and a mean over ~60 frames is the
    // quantity a before/after comparison can actually be made on.
    private int _frames;
    private double _calls;
    private double _prims;
    private double _msSum;
    private double _msWorst;

    // LD-2. The tracker feeds the clock; this only reads it. Null when there is no telemetry
    // autoload to hang the tracker on, in which case the label reads dashes rather than zeros.
    private CadenceTracker? _cadence;
    private Label? _cadenceLabel;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == Flag)
                _on = true;
        }
        SetProcess(_on);
        if (!_on)
            return;

        _cadence = CadenceTracker.Ensure();
        var layer = new CanvasLayer { Name = "CadenceReadout", Layer = UiLayers.PerfHud };
        _cadenceLabel = new Label { Position = new Vector2(18, 14) };
        _cadenceLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _cadenceLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _cadenceLabel.AddThemeConstantOverride("outline_size", 6);
        _cadenceLabel.AddThemeFontSizeOverride("font_size", 18);
        _cadenceLabel.Text = CadenceReadout.Lines(_cadence?.Clock);
        layer.AddChild(_cadenceLabel);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        _frames++;
        _calls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _prims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double ms = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _msSum += ms;
        if (ms > _msWorst)
            _msWorst = ms;

        // Every frame, not once a second: a since-act that only moved on the second would read
        // as a stuck clock for the first few seconds of watching it.
        CadenceClock? clock = _cadence?.Clock;
        if (_cadenceLabel != null)
            _cadenceLabel.Text = CadenceReadout.Lines(clock);

        _timer -= delta;
        if (_timer > 0 || _frames == 0)
            return;
        _timer = IntervalSec;

        float phase = CycleDriver.Instance is { Synced: true } d ? d.Phase : -1f;
        float glow = BubbleCounter.Instance?.CurrentEmissionEnergy ?? -1f;
        string cadence = clock is null
            ? "since_act=n/a stop_last_min=n/a stop_total=n/a last_act=n/a"
            : $"since_act={clock.SinceActSec:0.0} stop_last_min={clock.StopSecondsLastMinute:0.0} "
              + $"stop_total={clock.StopSecondsTotal:0.0} last_act={(clock.LastAct.Length == 0 ? "-" : clock.LastAct)}";
        GD.Print($"[bubbletest.perf] frames={_frames} "
                 + $"draw_calls_mean={_calls / _frames:0.0} "
                 + $"primitives_mean={_prims / _frames:0} "
                 + $"frame_ms_mean={_msSum / _frames:0.000} worst={_msWorst:0.000} "
                 + $"vram_mb={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0.0} "
                 + $"phase={phase:0.000} film_emission_energy={glow:0.000} "
                 + cadence);

        _frames = 0;
        _calls = 0;
        _prims = 0;
        _msSum = 0;
        _msWorst = 0;
    }
}
