using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The per-frame frame-pacing instrument</b> (SICK-1, 2026-09-20). Records, on every RENDER
/// frame, the wall time the frame took and where the first-person lens was and which way it
/// looked, then writes a CSV and one summary line and stops.
///
/// <para><b>Why a new instrument rather than PROBE-1's.</b> PROBE-1's <c>BotHarness</c> reading
/// (<c>prs</c>/<c>fms</c>/<c>draws</c>/<c>objs</c>, commit <c>71f7606</c>) is a 5 Hz SPOT sample
/// and its <c>fms</c> is derived from <c>TimeFps</c>, which is an AVERAGE. Both choices are right
/// for the question PROBE-1 asked — what does N props cost — and both destroy the question this
/// lane asks. Judder is a property of the distribution of consecutive frames: a run whose average
/// frame time is a flawless 6.1 ms can be alternating 3 ms and 9 ms, and a 5 Hz average cannot
/// tell those apart. The four PROBE-1 fields are COPIED into the summary line below (they are
/// three lines of <c>Performance.GetMonitor</c>) rather than the commit being cherry-picked,
/// because that commit also carries <c>ProbeSeedLayout</c>, <c>PropManager</c>'s seeding and
/// <c>ReachCostProbe</c>, none of which this lane touches and all of which sit in files other
/// lanes are live in.</para>
///
/// <para><b>It is built only when <c>--pacing-log</c> is given</b> and it is the only thing in
/// this lane that allocates per frame, which is exactly the always-on-rig objection
/// <c>ReachCostProbe</c>'s own header makes. Two pre-sized lists and no string work until the
/// run ends, so the rig's own cost stays out of the number it is reporting.</para>
/// </summary>
public partial class CameraPacingProbe : Node
{
    /// <summary>The lens to follow. Its <c>CameraNode</c> is what is actually sampled — the rig
    /// node and the lens share an origin, but the lens is the thing that renders.</summary>
    public required FirstPersonCamera Camera { get; init; }

    /// <summary>Where the CSV goes. One row per render frame.</summary>
    public required string CsvPath { get; init; }

    /// <summary>Seconds to record before writing and printing. The run keeps going; only the
    /// recording stops, so a capture or a self-test after the window is unaffected.</summary>
    public required double DurationSec { get; init; }

    /// <summary><b>The constant scripted turn</b>, degrees per second, 0 for none. Driven off
    /// ELAPSED WALL TIME rather than accumulated per frame, because that is what a mouse moved at
    /// a constant speed actually delivers: the device reports at ~1 kHz, so the yaw a frame is
    /// handed is proportional to how long that frame took. Accumulating <c>rate × delta</c>
    /// instead would hand every frame an identical angle and make the one thing this is here to
    /// measure — whether a steady turn arrives evenly — true by construction.</summary>
    public float TurnDegPerSec { get; init; }

    /// <summary>A label for the summary line, so a table row and a log line can be matched up.</summary>
    public string Label { get; init; } = "run";

    private readonly List<float> _frameMs = new(16384);
    private readonly List<float> _stepM = new(16384);
    private readonly List<float> _yawStepDeg = new(16384);
    private readonly List<Vector3> _eye = new(16384);

    private ulong _lastUsec;
    private double _elapsed;
    private float _startYaw;
    private bool _primed;
    private bool _done;

    public override void _Ready()
    {
        // ALWAYS after the camera. This node is a sibling added to the same parent the camera
        // hangs off, and Godot runs _Process in tree order, so "added later" is "runs later" —
        // which is what makes a sample taken here the FINAL position of the lens this frame
        // rather than its position halfway through the frame's own updates.
        ProcessPriority = 1000;
        _startYaw = Camera.Yaw;
        _lastUsec = Time.GetTicksUsec();
        GD.Print($"[pacing] recording '{Label}' for {DurationSec:F0}s -> {CsvPath} "
                 + $"(turn {TurnDegPerSec:F1} deg/s, "
                 + $"refresh {DisplayServer.ScreenGetRefreshRate():F1} Hz, "
                 + $"vsync {DisplayServer.WindowGetVsyncMode()}, "
                 + $"max_fps {Engine.MaxFps}, "
                 + $"physics {Engine.PhysicsTicksPerSecond} Hz, "
                 + $"cam-interp {FirstPersonCamera.InterpolateToRenderFrame})");
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;

        if (TurnDegPerSec != 0f)
            Camera.SetLook(_startYaw + Mathf.DegToRad(TurnDegPerSec * (float)_elapsed), Camera.Pitch);

        ulong now = Time.GetTicksUsec();
        float ms = (now - _lastUsec) / 1000f;
        _lastUsec = now;
        _elapsed += delta;

        Camera3D? lens = Camera.CameraNode;
        if (lens == null || !GodotObject.IsInstanceValid(lens))
            return;
        Vector3 p = lens.GlobalPosition;
        float yawDeg = Mathf.RadToDeg(Camera.Yaw);

        // The first frame has no predecessor, so it has no step and its "frame time" is however
        // long _Ready happened to be from it. Dropped rather than recorded as a zero step, which
        // would bias the one statistic this whole rig exists to produce.
        if (_primed)
        {
            _frameMs.Add(ms);
            _stepM.Add((p - _eye[^1]).Length());
            _yawStepDeg.Add(Mathf.Abs(yawDeg - _lastYawDeg));
        }
        _eye.Add(p);
        _lastYawDeg = yawDeg;
        _primed = true;

        if (_elapsed >= DurationSec)
            Finish();
    }

    private float _lastYawDeg;

    private void Finish()
    {
        _done = true;
        CameraPacing.Summary s = CameraPacing.Summarize(_frameMs, _stepM, _yawStepDeg);

        string? dir = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using (var w = new StreamWriter(File.Open(CsvPath, FileMode.Create, System.IO.FileAccess.Write,
                   FileShare.Read)))
        {
            w.WriteLine("frame,frame_ms,eye_x,eye_y,eye_z,step_m,yaw_step_deg");
            for (int i = 0; i < _frameMs.Count; i++)
            {
                Vector3 e = _eye[i + 1];
                w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i},{_frameMs[i]:F4},{e.X:F5},{e.Y:F5},{e.Z:F5},{_stepM[i]:F6},{_yawStepDeg[i]:F5}"));
            }
        }

        // PROBE-1's four render-side numbers, copied (see the class doc for why copied and not
        // cherry-picked). They are a spot reading taken once, at the end of the window: they are
        // here so a pacing row can be read next to a draw-call count, not as a second pacing
        // measurement, and the two must never be confused.
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        GD.Print($"[pacing] SUMMARY label={Label} frames={s.Frames} "
                 + $"dt_p50={s.P50Ms:F3}ms dt_p95={s.P95Ms:F3}ms dt_max={s.MaxMs:F3}ms "
                 + $"over20ms={s.OverBudget} "
                 + $"zero_step={s.ZeroStepFraction * 100f:F1}% "
                 + $"step_mean={s.MeanStepM * 1000f:F3}mm step_sd={s.StepStdDevM * 1000f:F3}mm "
                 + $"yaw_sd={s.YawStepStdDevDeg:F4}deg "
                 + $"| prs={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:F3}ms "
                 + $"fms={(fps > 0.0 ? 1000.0 / fps : 0.0):F3}ms "
                 + $"draws={(int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} "
                 + $"objs={(int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}");
        GD.Print($"[pacing] csv {CsvPath} ({_frameMs.Count} rows)");
    }
}
