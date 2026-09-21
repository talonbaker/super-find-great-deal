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
    // The owner's undrained reconciliation offset this frame (SandboxAvatar.VisualErrorM) and the
    // raw body position beside it. Recorded because "the eye jumped" and "the eye jumped BECAUSE a
    // correction landed that every other tracker absorbed" are different findings, and only the
    // second one names a fix. See the class doc of SandboxAvatar.RenderGlobalPosition.
    private readonly List<float> _visErr = new(16384);
    private readonly List<Vector3> _body = new(16384);

    // STALL-1, 2026-09-21: the four columns that tell a ~535 ms frame apart from the three things
    // it could be. All four are single cheap reads (two counters and two doubles the engine has
    // already computed for the frame that just ended), so the instrument's own cost does not move.
    //
    //   gc0/1/2     System.GC.CollectionCount. A managed pause shows up as a generation counter
    //               stepping on EXACTLY the stall frame. SICK-1 could not rule GC out.
    //   physTicks   How many physics ticks the engine ran for this render frame. A render stall
    //               with catch-up reads as 8 (max_physics_steps_per_frame); a physics blow-up
    //               reads as 1 with a huge phys_ms. Those are opposite findings and the CSV had
    //               no column that could tell them apart.
    //   procMs      TimeProcess, and physMs TimePhysicsProcess: the time the engine spent INSIDE
    //               its own frame. frame_ms minus (procMs + physMs) is time spent somewhere the
    //               engine is not — present, swapchain acquire, the driver — which is the whole
    //               question this lane asks.
    private readonly List<int> _gc0 = new(16384);
    private readonly List<int> _gc1 = new(16384);
    private readonly List<int> _gc2 = new(16384);
    private readonly List<int> _physTicks = new(16384);
    private readonly List<float> _procMs = new(16384);
    private readonly List<float> _physMs = new(16384);
    private ulong _lastPhysicsFrame;

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
        _lastPhysicsFrame = Engine.GetPhysicsFrames();
        PrintAdapterFacts();
        GD.Print($"[pacing] recording '{Label}' for {DurationSec:F0}s -> {CsvPath} "
                 + $"(turn {TurnDegPerSec:F1} deg/s, "
                 + $"refresh {DisplayServer.ScreenGetRefreshRate():F1} Hz, "
                 + $"vsync {DisplayServer.WindowGetVsyncMode()}, "
                 + $"max_fps {Engine.MaxFps}, "
                 + $"physics {Engine.PhysicsTicksPerSecond} Hz, "
                 + $"cam-interp {FirstPersonCamera.InterpolateToRenderFrame})");
    }

    /// <summary>
    /// <b>Which adapter is drawing this window, and on what</b> (STALL-1, 2026-09-21). Printed
    /// once, from the probe rather than from a boot path, because the probe is built only on a
    /// run that is being measured and this is a fact ABOUT that measurement: a pacing row taken
    /// on a different adapter, driver, method or screen mode than the row beside it is not
    /// comparable to it, and until this line existed nothing in a log said which.
    ///
    /// <para><b>It is meaningless on a headless peer</b> — there is no rendering device and no
    /// screen — which is itself the point SICK-1's headless control rests on, so the line prints
    /// what it can and says <c>headless</c> rather than being suppressed.</para>
    /// </summary>
    private static void PrintAdapterFacts()
    {
        bool headless = DisplayServer.GetName() == "headless";
        string driverInfo = string.Join(" / ", OS.GetVideoAdapterDriverInfo());
        int screen = headless ? 0 : DisplayServer.WindowGetCurrentScreen();
        Vector2I screenSize = headless ? Vector2I.Zero : DisplayServer.ScreenGetSize(screen);
        Vector2I winSize = headless ? Vector2I.Zero : DisplayServer.WindowGetSize();
        GD.Print($"[adapter] display_server={DisplayServer.GetName()} "
                 + $"rendering_driver={RenderingServer.GetCurrentRenderingDriverName()} "
                 + $"rendering_method={RenderingServer.GetCurrentRenderingMethod()} "
                 + $"adapter='{RenderingServer.GetVideoAdapterName()}' "
                 + $"vendor='{RenderingServer.GetVideoAdapterVendor()}' "
                 + $"type={RenderingServer.GetVideoAdapterType()} "
                 + $"api={RenderingServer.GetVideoAdapterApiVersion()} "
                 + $"driver_info='{driverInfo}'");
        GD.Print($"[adapter] screens={(headless ? 0 : DisplayServer.GetScreenCount())} "
                 + $"screen={screen} screen_size={screenSize.X}x{screenSize.Y} "
                 + $"refresh={(headless ? 0f : DisplayServer.ScreenGetRefreshRate(screen)):F1}Hz "
                 + $"window_size={winSize.X}x{winSize.Y} "
                 + $"window_mode={(headless ? "-" : DisplayServer.WindowGetMode().ToString())} "
                 + $"vsync={(headless ? "-" : DisplayServer.WindowGetVsyncMode().ToString())} "
                 + $"max_fps={Engine.MaxFps} physics_hz={Engine.PhysicsTicksPerSecond} "
                 + $"max_physics_steps={ProjectSettings.GetSetting("physics/common/max_physics_steps_per_frame", 8)} "
                 + $"gc_server={System.Runtime.GCSettings.IsServerGC} "
                 + $"gc_latency={System.Runtime.GCSettings.LatencyMode}");
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
        SandboxAvatar? body = Camera.Target;
        float visErr = body != null && GodotObject.IsInstanceValid(body) ? body.VisualErrorM : 0f;
        Vector3 bodyPos = body != null && GodotObject.IsInstanceValid(body) ? body.GlobalPosition : Vector3.Zero;

        // The first frame has no predecessor, so it has no step and its "frame time" is however
        // long _Ready happened to be from it. Dropped rather than recorded as a zero step, which
        // would bias the one statistic this whole rig exists to produce.
        ulong physNow = Engine.GetPhysicsFrames();
        int ticks = (int)(physNow - _lastPhysicsFrame);
        _lastPhysicsFrame = physNow;

        if (_primed)
        {
            _frameMs.Add(ms);
            _stepM.Add((p - _eye[^1]).Length());
            _yawStepDeg.Add(Mathf.Abs(yawDeg - _lastYawDeg));
            // STALL-1's four discriminating columns. Recorded on the SAME frames as frame_ms so a
            // row of the CSV is one frame's whole story; see the field declarations for what each
            // one rules in or out.
            _gc0.Add(System.GC.CollectionCount(0));
            _gc1.Add(System.GC.CollectionCount(1));
            _gc2.Add(System.GC.CollectionCount(2));
            _physTicks.Add(ticks);
            _procMs.Add((float)(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0));
            _physMs.Add((float)(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0));
        }
        _eye.Add(p);
        _visErr.Add(visErr);
        _body.Add(bodyPos);
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
        float peakVisErr = 0f;
        int corrections = 0;
        for (int i = 1; i < _visErr.Count; i++)
        {
            if (_visErr[i] > peakVisErr)
                peakVisErr = _visErr[i];
            // A correction LANDING is the frame the offset grows; it shrinks on every other frame
            // as OwnerTick drains it. Counting the growths counts the pops rather than the frames
            // spent recovering from them.
            if (_visErr[i] > _visErr[i - 1] + 0.05f)
                corrections++;
        }

        string? dir = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using (var w = new StreamWriter(File.Open(CsvPath, FileMode.Create, System.IO.FileAccess.Write,
                   FileShare.Read)))
        {
            w.WriteLine("frame,frame_ms,eye_x,eye_y,eye_z,step_m,yaw_step_deg,visual_error_m,body_x,body_z"
                        + ",gc0,gc1,gc2,phys_ticks,proc_ms,phys_proc_ms");
            for (int i = 0; i < _frameMs.Count; i++)
            {
                Vector3 e = _eye[i + 1];
                Vector3 b = _body[i + 1];
                w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i},{_frameMs[i]:F4},{e.X:F5},{e.Y:F5},{e.Z:F5},{_stepM[i]:F6},{_yawStepDeg[i]:F5}"
                    + $",{_visErr[i + 1]:F5},{b.X:F5},{b.Z:F5}"
                    + $",{_gc0[i]},{_gc1[i]},{_gc2[i]},{_physTicks[i]},{_procMs[i]:F4},{_physMs[i]:F4}"));
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
                 + $"pops={corrections} peak_verr={peakVisErr:F3}m "
                 + $"| prs={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:F3}ms "
                 + $"fms={(fps > 0.0 ? 1000.0 / fps : 0.0):F3}ms "
                 + $"draws={(int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} "
                 + $"objs={(int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}");
        GD.Print($"[pacing] csv {CsvPath} ({_frameMs.Count} rows)");
        PrintStallLine();
    }

    /// <summary>
    /// <b>The stall row, computed here so a table row does not depend on anyone re-deriving it
    /// from the CSV</b> (STALL-1, 2026-09-21). SICK-1 §5 recorded "17–18 frames of ~535 ms in a
    /// 36 s recording" by eye off the raw file; this prints the same three quantities — how many,
    /// how big, how far apart — plus the two columns that say what a stall frame WAS.
    ///
    /// <para>The 100 ms bar is SICK-1's own cut and is deliberately far above both the 16.7 ms
    /// frame and the 50.6 ms worst headless frame, so it cannot be tripped by ordinary jitter on
    /// either side of the comparison.</para>
    /// </summary>
    private void PrintStallLine()
    {
        const float StallMs = 100f;
        int n = 0;
        float sum = 0f, max = 0f;
        int gcOnStall = 0, eightTickStalls = 0;
        int lastIdx = -1;
        double periodSum = 0.0;
        int periodN = 0;
        double sinceLast = 0.0;
        for (int i = 0; i < _frameMs.Count; i++)
        {
            sinceLast += _frameMs[i] / 1000.0;
            if (_frameMs[i] < StallMs)
                continue;
            n++;
            sum += _frameMs[i];
            if (_frameMs[i] > max)
                max = _frameMs[i];
            if (i > 0 && (_gc0[i] > _gc0[i - 1] || _gc1[i] > _gc1[i - 1] || _gc2[i] > _gc2[i - 1]))
                gcOnStall++;
            if (_physTicks[i] >= 8)
                eightTickStalls++;
            if (lastIdx >= 0)
            {
                periodSum += sinceLast;
                periodN++;
            }
            lastIdx = i;
            sinceLast = 0.0;
        }
        double wall = 0.0;
        for (int i = 0; i < _frameMs.Count; i++)
            wall += _frameMs[i] / 1000.0;
        int gcTotal0 = _gc0.Count > 0 ? _gc0[^1] - _gc0[0] : 0;
        int gcTotal1 = _gc1.Count > 0 ? _gc1[^1] - _gc1[0] : 0;
        int gcTotal2 = _gc2.Count > 0 ? _gc2[^1] - _gc2[0] : 0;
        GD.Print($"[stall] label={Label} window={wall:F1}s stalls>{StallMs:F0}ms={n} "
                 + $"mean={(n > 0 ? sum / n : 0f):F1}ms max={max:F1}ms "
                 + $"period={(periodN > 0 ? periodSum / periodN : 0.0):F2}s "
                 + $"on_8_ticks={eightTickStalls} with_gc={gcOnStall} "
                 + $"gc_in_window=[{gcTotal0},{gcTotal1},{gcTotal2}]");
    }
}
