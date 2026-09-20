using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The Bible §7 measurement layer: every §4 budget is verified IN the deployment scene,
/// so the deployment scene carries its own meter. F3 toggles a corner readout (fps,
/// frame ms, draw calls, primitives, VRAM, nodes); with <c>--perf-log path</c> it also
/// appends one JSON line per second so profiling runs are machine-readable (that's how
/// the numbers in PR bodies are produced). Costs one Label updated 4x/s while visible
/// and nothing while hidden.
/// </summary>
public partial class PerfHud : CanvasLayer
{
    private const double RefreshSec = 0.25;
    private const double LogIntervalSec = 1.0;

    /// <summary>The instrument's colour. See the note where it is applied: this readout is
    /// deliberately not part of the interface, and looking like it would be the defect.</summary>
    private static readonly Color DiagnosticGreen = new(0.7f, 1f, 0.75f);

    private Label _label = null!;
    private double _sinceRefresh;
    private double _sinceLog;
    private Godot.FileAccess? _log;

    public override void _Ready()
    {
        Layer = Design.UiLayers.PerfHud;
        _label = new Label
        {
            // Below the HUD's top-left tally panel (2026-08-08), which now owns that corner.
            // This readout is on layer 90 and the HUD on 80, so at the old y=12 the F3 text drew
            // straight over the player's balance — dev-only, but it made the meter unreadable
            // exactly when someone was using it to read numbers.
            Position = new Vector2(12, 104),
            ThemeTypeVariation = "PerfHud",
            Visible = false,
        };
        // Deliberately OUTSIDE the palette. This is an instrument, not interface: a developer
        // glancing at it must never wonder whether it is part of the game, and a diagnostic that
        // adopted the interface's ink would be exactly that ambiguous. Size and family come from
        // the PerfHud variation; the terminal green is the one colour in the project that is
        // allowed not to be a token, and it is allow-listed by name in the no-literals guard.
        _label.AddThemeColorOverride("font_color", DiagnosticGreen);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _label.AddThemeConstantOverride("outline_size", Design.UiScale.BorderFocus);
        AddChild(_label);

        string logPath = NetworkManager.Instance.Options.PerfLog;
        if (logPath.Length > 0)
            _log = Godot.FileAccess.Open(logPath, Godot.FileAccess.ModeFlags.Write);
    }

    public override void _ExitTree()
    {
        _log?.Close();
        _log = null;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 })
            _label.Visible = !_label.Visible;
    }

    public override void _Process(double delta)
    {
        if (_log != null)
        {
            _sinceLog += delta;
            if (_sinceLog >= LogIntervalSec)
            {
                _sinceLog = 0;
                _log.StoreLine(Json.Stringify(Snapshot()));
                _log.Flush();
            }
        }

        if (!_label.Visible)
            return;
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshSec)
            return;
        _sinceRefresh = 0;

        Godot.Collections.Dictionary s = Snapshot();
        _label.Text =
            $"fps {s["fps"]}  frame {s["frame_ms"]} ms\n" +
            $"draw calls {s["draw_calls"]}  (budget 350, ceiling 600)\n" +
            $"primitives {s["primitives"]}\n" +
            $"vram {s["vram_mb"]} MB   nodes {s["nodes"]}";
    }

    private static Godot.Collections.Dictionary Snapshot() => new()
    {
        ["fps"] = Performance.GetMonitor(Performance.Monitor.TimeFps),
        ["frame_ms"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0, 0.01),
        ["physics_ms"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0, 0.01),
        ["draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        ["primitives"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
        ["vram_mb"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0), 0.1),
        ["nodes"] = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
        ["static_mem_mb"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0), 0.1),
    };
}
