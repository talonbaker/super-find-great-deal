using System.Globalization;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>BIKE-2x's wiring, and the only place it touches the harness</b> (2026-09-02). The handling
/// model, the bike camera, the knob panel and the telemetry all hang off exactly two call sites in
/// <c>MovementPlayground.cs</c> — one line in <c>_Ready</c> and one at the end of
/// <c>_PhysicsProcess</c> — so this packet's whole footprint in the first agent's file is two
/// lines, and everything else lives here.
///
/// <para><b>What this file is allowed to write, and what it is not.</b> It writes the bike's own
/// tuning records (through their own single writers), the greybox's roll, its own readout label,
/// and the telemetry file. <b>It never writes <c>_avatar.Velocity</c>.</b> The drift's exit boost
/// is computed, accumulated as an owed impulse, shown on the readout and logged — and it stays owed
/// until <c>BikeLayer.RequestImpulse</c> lands on this branch. A second writer of the body's
/// velocity is the exact defect the file ownership on this packet exists to prevent, and an owed
/// number a human can read is a better artefact than a body that moved for a reason nothing
/// records.</para>
///
/// <para><b>Two writers, one transform, and how they are kept apart.</b> Two values here are also
/// written by somebody else every frame: the greybox's rotation (by <c>BikeGreybox.Track</c>, on the
/// render clock) and <c>BikeTuning.Current.RideTurnMul</c> (by the ALT preset row and by the knob
/// panel). Both are handled the same way — read what the other writer left, derive from THAT, and
/// write on the same clock they do. The greybox's roll is applied by <see cref="BikeLeanRig"/> at a
/// process priority above the harness's so it lands after <c>Track</c>; the turn multiplier keeps
/// the exact value it last wrote and treats any difference from it as somebody else's write, which
/// it then adopts as the new base. The repo has already paid once for a transform written on one
/// clock by a node that also wrote it on another — see <c>MovementPlayground._Ready</c>'s note about
/// the removed tumble.</para>
/// </summary>
public partial class MovementPlayground
{
    // --- Nodes this packet owns --------------------------------------------------------------------

    private BikeCamera? _bikeCamera;
    private BikeKnobPanel? _bikeKnobs;
    private BikeTelemetry? _bikeTelemetry;
    private BikeLeanRig? _leanRig;

    /// <summary><b>A SECOND readout label, deliberately, rather than extra lines on the harness's
    /// own.</b> <c>UpdateReadout</c> is one interpolated string in the first agent's file and this
    /// packet does not own it. A separate label in the bottom-right costs one node and keeps the
    /// footprint at two lines; it also keeps the handling readout together in one block instead of
    /// scattered through the motor's.</summary>
    private Label? _bikeHandlingReadout;

    /// <summary><c>--bike-handling-selftest</c> was passed after the <c>--</c>.</summary>
    private bool _bikeHandlingSelfTest;

    // --- Handling state ------------------------------------------------------------------------------

    /// <summary>The lean the greybox is drawn at, degrees, positive = leaning left. Presentation
    /// only; nothing reads it back into the simulation.</summary>
    private float _leanDeg;

    /// <summary><b>The low-speed wobble this tick, degrees</b> (BIKE-4B). Presentation only, and
    /// kept as its own field rather than folded into <see cref="_leanDeg"/> on purpose: the corner
    /// lean stays exactly the number BIKE-3A measured and every check that reads it keeps reading
    /// the same quantity, while the wobble is a term anyone can see, log and zero on its own.</summary>
    private float _wobbleDeg;

    /// <summary>The wobble's local clock, seconds since the current mount. Advances only while
    /// mounted and resets to zero on dismount, so a mount always starts from an upright bike —
    /// both of <c>WobbleDeg</c>'s sines are zero at phase zero. Local and unreplicated: nothing
    /// about the wobble goes on the wire.</summary>
    private float _wobblePhaseSec;

    /// <summary><b>The roll the greybox is actually drawn at, degrees</b> = corner lean + wobble.
    /// This, not <see cref="_leanDeg"/>, is what <see cref="BikeLeanRig"/> reads. It is read by the
    /// rig, by the readout and by the self-test, and by nothing that writes velocity, tuning or the
    /// drift state — which is the whole of BIKE-4B's acceptance criterion 5.</summary>
    private float _rollDeg;

    /// <summary>The body's yaw last tick, for the yaw rate the lean is a function of.</summary>
    private float _prevYawRad;

    /// <summary>The yaw rate measured last tick, rad/s — kept for the readout and the telemetry so
    /// both quote the number the lean was actually computed from.</summary>
    private float _yawRatePerSec;

    private BikeHandling.DriftState _drift = BikeHandling.DriftState.Rest;

    /// <summary>The tier the live drift has reached, 0..3. Drives the greybox spark's colour and
    /// nothing else — the packet forbids a counter, so the colour IS the readout for the player.</summary>
    private int _driftTierNow;

    /// <summary>Drifts entered this session, and the best tier any of them reached.</summary>
    private int _driftEntries;
    private int _driftPeakTier;

    /// <summary><b>The impulse the drift has earned and not been given.</b> Every exit boost this
    /// session, summed. It is shown on the readout and written to the telemetry precisely because it
    /// has NOT been applied: the seam that would apply it is not on this branch, and a number nobody
    /// can see is a feature nobody can tell is missing.</summary>
    private float _owedBoostMps;

    /// <summary>The boost the most recent exit was worth, and the tier it came off.</summary>
    private float _lastBoostMps;
    private int _lastBoostTier;

    /// <summary>A drift press from a script (the self-test). ORed with the real right mouse button,
    /// so a scripted run drives the same model a hand does.</summary>
    private bool _scriptedDriftHeld;

    // --- Turn shaping: this packet is the SECOND writer of RideTurnMul ---------------------------------

    /// <summary>The last value this file wrote into <c>RideTurnMul</c>. NaN means "nothing written
    /// yet". Compared with exact equality on purpose: this is the value we ourselves produced, so if
    /// the live row differs from it by even a bit, somebody else wrote it (an ALT preset, the knob
    /// panel) and their number becomes the new base. An approximate comparison here would make a
    /// knob dragged by one step invisible to the adoption test and silently multiply it away.</summary>
    private float _shapedTurnMulWritten = float.NaN;

    /// <summary>The unshaped <c>RideTurnMul</c> the curve multiplies. Adopted from whatever the other
    /// writers leave behind.</summary>
    private float _baseTurnMul = BikeTuning.Default.RideTurnMul;

    /// <summary>The turn multiplier applied on the last tick, for the readout.</summary>
    private float _turnMulNow = 1f;

    // --- Wiring ----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Call site one of two.</b> Everything this packet adds to the scene is built here, after
    /// the harness has built the avatar, the camera, the greybox and the tuning panel — it binds to
    /// all four.
    /// </summary>
    private void BikeHandlingReady()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--bike-handling-selftest")
                _bikeHandlingSelfTest = true;
            // BIKE-2x-L: the headed capture run (MovementPlayground.BikeCapture.cs).
            if (a == "--bike-capture")
                _bikeCapture = true;
            // BIKE-4B: the wobble's frame sequences (MovementPlayground.BikeWobbleCapture.cs).
            // Passed ALONGSIDE --bike-capture, never instead of it — see that file's class doc:
            // BikeScriptedFlagPresent matches the plain flag exactly and is what keeps the human
            // tuning file and the knob panel out of the photographed frame.
            if (a == "--bike-capture-wobble")
                _bikeWobbleCapture = true;
        }
        if (_bikeCapture)
        {
            _bikeCaptureDir = OS.GetEnvironment("SAIL_BIKE_CAPTURE_OUT");
            if (_bikeCaptureDir.Length == 0)
            {
                GD.PrintErr("--bike-capture needs SAIL_BIKE_CAPTURE_OUT (absolute output dir)");
                GetTree().Quit(1);
                return;
            }
        }

        bool scripted = _outDir.Length > 0 || _bikeSelfTest || _bikeHandlingSelfTest || _bikeCapture;
        if ((_bikeHandlingSelfTest || _bikeCapture) && _scripted is null)
            BuildScriptedBrainForSelfTest();

        // The camera decorates the shipped SandboxCamera; it is inert until the bike is out.
        if (_bike is not null)
        {
            _bikeCamera = new BikeCamera { Name = "BikeCamera" };
            AddChild(_bikeCamera);
            _bikeCamera.Bind(_camera, _bike, _avatar);
        }

        // The lean rig writes the greybox's roll on the RENDER clock, above the harness's own
        // priority, so it lands after BikeGreybox.Track has rewritten the same rotation.
        if (_bikeMesh is not null)
        {
            _leanRig = new BikeLeanRig { Name = "BikeLeanRig" };
            AddChild(_leanRig);
            // The rig draws the composed roll (corner lean + low-speed wobble), not the corner lean
            // alone. BIKE-4B: the wobble's ONE consumer is this line.
            _leanRig.Bind(_bikeMesh, () => _rollDeg, () => _driftTierNow,
                () => _drift.Active ? BikeHandling.TierProgress01(_drift.ChargeSec,
                    BikeHandlingTuning.Current) : 0f);
        }

        _bikeKnobs = new BikeKnobPanel
        {
            Name = "BikeKnobPanel",
            Bike = _bike,
            StartVisible = false,
            Notice = line => _tuning?.Session.Note(line),
        };
        AddChild(_bikeKnobs);

        _bikeTelemetry = new BikeTelemetry { Name = "BikeTelemetry" };
        AddChild(_bikeTelemetry);
        _bikeTelemetry.Begin(scripted ? "scripted run" : "hands-on lab session");

        BuildBikeHandlingReadout();

        _prevYawRad = _avatar.GlobalRotation.Y;
        _baseTurnMul = BikeTuning.Current.RideTurnMul;

        if (_bikeHandlingSelfTest)
            _ = RunBikeHandlingSelfTestAsync();
        else if (_bikeWobbleCapture)
            _ = RunBikeWobbleCaptureAsync();
        else if (_bikeCapture)
            _ = RunBikeCaptureAsync();
    }

    /// <summary>
    /// <b>Give this packet's self-test the scripted brain it needs, from this packet's own file.</b>
    ///
    /// <para><b>Why it has to be rebuilt rather than asked for.</b> <c>BuildAvatarAndCamera</c>
    /// decides which brain the avatar gets, and it decides on <c>_outDir</c> and
    /// <c>_bikeSelfTest</c> — both of which are read in <c>_Ready</c> BEFORE it runs. This packet's
    /// flag is parsed here, which is after, so on a <c>--bike-handling-selftest</c> run the harness
    /// has already wired the keyboard brain and <c>_scripted</c> is null.</para>
    ///
    /// <para><b>It cost eleven minutes of wall clock to find, and the way it failed is the lesson.</b>
    /// The self-test dereferenced that null, the exception vanished into an un-awaited
    /// <c>Task</c>, and the scene simply <i>kept running</i> — no error, no exit, one Godot process
    /// at 100 % of a core until it was killed. A headless run that hangs looks exactly like a
    /// headless run that is slow. That is why <see cref="RunBikeHandlingSelfTestAsync"/> now wraps
    /// its whole body in a catch that prints and exits 1.</para>
    ///
    /// <para>Rebuilding here rather than adding a third line to <c>_Ready</c> keeps this packet's
    /// footprint in the first agent's file at the two lines it was scoped to. The brain is the same
    /// <c>ScriptedInput</c> the harness would have built, wrapped in the same <c>BikeLayer</c>, so
    /// the self-test drives the production seam and not a second input path.</para>
    /// </summary>
    private void BuildScriptedBrainForSelfTest()
    {
        _scripted = new ScriptedInput();
        _bike = new BikeLayer(_scripted, _avatar) { Scripted = true };
        _avatar.IntentSource = _bike;
        // Re-point the notice sink that BuildTuningPanel wired onto the layer this one replaces.
        if (_tuning is not null)
            _bike.Notice = line => _tuning.Session.Note(line);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void BuildBikeHandlingReadout()
    {
        var layer = new CanvasLayer { Name = "BikeHandlingReadout" };
        AddChild(layer);
        _bikeHandlingReadout = new Label { Name = "Text" };
        layer.AddChild(_bikeHandlingReadout);
        // Bottom-right: the motor readout owns the top-left and the preset banner the bottom-left,
        // and the knob panel is a left-hand column. Nothing else is here.
        _bikeHandlingReadout.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        // Sized for all seven lines at this font: the first cut (-132 tall, -720 wide) was
        // shorter than the text, and the HANDLING/drift/owed lines — the ones a capture frame
        // exists to carry — rendered outside the rect in every take-2 frame.
        _bikeHandlingReadout.OffsetLeft = -1150;
        _bikeHandlingReadout.OffsetRight = -16;
        _bikeHandlingReadout.OffsetTop = -224;
        _bikeHandlingReadout.OffsetBottom = -10;
        _bikeHandlingReadout.HorizontalAlignment = HorizontalAlignment.Right;
        _bikeHandlingReadout.AddThemeColorOverride("font_color", new Color(0.72f, 0.96f, 0.84f));
        _bikeHandlingReadout.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _bikeHandlingReadout.AddThemeConstantOverride("outline_size", 6);
        _bikeHandlingReadout.AddThemeFontSizeOverride("font_size", 13);
    }

    /// <summary>
    /// <b>Call site two of two.</b> Runs at the end of the harness's <c>_PhysicsProcess</c>, which
    /// is after the avatar has stepped AND after <c>BikeLayer.PostStep</c> — so every value read
    /// here is this tick's settled answer rather than a half-stepped one.
    /// </summary>
    private void BikeHandlingPhysics(double delta)
    {
        float dt = (float)delta;
        BikeHandlingTuning h = BikeHandlingTuning.Current;

        bool mounted = _bike?.Mounted ?? false;
        bool grounded = _avatar.Grounded;
        float speed = new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();

        // --- lean ------------------------------------------------------------------------------
        float yaw = _avatar.GlobalRotation.Y;
        _yawRatePerSec = BikeHandling.YawRatePerSec(yaw, _prevYawRad, dt);
        _prevYawRad = yaw;
        // Off the bike the target is upright: a body on foot does not lean, and a lean left over
        // from a dismount would be drawn on a bike that is folded away on its back.
        float leanTarget = mounted ? BikeHandling.LeanSteadyDeg(speed, _yawRatePerSec, h) : 0f;
        _leanDeg = BikeHandling.LeanStep(_leanDeg, leanTarget, dt, h);

        // --- the low-speed wobble, layered UNDER the corner lean (BIKE-4B) ----------------------
        // Added to the DRAWN roll rather than to `leanTarget`, and that placement is the design,
        // not a convenience. `LeanStep` is a first-order lag at 8 /s — a low-pass whose corner is
        // near 1.3 Hz — so a wobble fed through it would arrive at roughly half size and phase-
        // shifted, the amplitude knob would no longer mean degrees, and BIKE-3A's measured corner
        // lean would stop being the number it measured. Composing after the lag keeps `_leanDeg`
        // bit-for-bit what it was and makes the knob honest.
        //
        // The phase is held at zero off the bike so every mount begins upright.
        _wobblePhaseSec = mounted ? _wobblePhaseSec + dt : 0f;
        _wobbleDeg = mounted ? BikeHandling.WobbleDeg(speed, _wobblePhaseSec, h) : 0f;
        _rollDeg = _leanDeg + _wobbleDeg;

        // --- the drift ---------------------------------------------------------------------------
        // The drift button is asked of BIKE-0's layer, which already polls it, rather than polled
        // a second time here.
        //
        // THIS LINE WAS WRONG UNTIL TALON'S FIRST SESSION FOUND IT, and the way it was wrong is
        // worth the paragraph. It used to read
        // `_scriptedDriftHeld || (!_avatar.Scripted() && Input.IsMouseButtonPressed(Right))`,
        // where `Scripted()` asked `IIntentSource.IsHumanInput`. But `BikeLayer` deliberately does
        // NOT declare that property (its own comment at BikeLayer.cs:188 explains why — the repo's
        // achievement guard lets only the real keyboard source claim a person), so it falls through
        // to the interface's `=> false` default. In a hands-on session the avatar's intent source
        // IS the BikeLayer, so `Scripted()` returned TRUE for a human at a keyboard and the whole
        // right-hand side was dead. **The drift was unreachable by hand.** Every tier, every charge
        // model, every spark and the exit boost were unreachable with it.
        //
        // The self-test passed 12/12 the entire time, because it drives `_scriptedDriftHeld` and
        // never took the branch that was broken — a test exercising the path that works while the
        // path a player uses is dead. What caught it was the telemetry: Talon's first session
        // logged ZERO drift entries across 104 seconds above the 3.5 m/s gate, which is not a
        // preference, it is an impossibility. `BikeTelemetry` earned its keep on day one.
        //
        // `BikeLayer.Drifting` is `Mounted && the button && on the floor`, so it is false in the
        // air and off the bike — which is the same answer the mounted/grounded arguments below
        // give, and one source of truth for "is the button down" instead of two.
        bool driftHeld = _scriptedDriftHeld || (_bike?.Drifting ?? false);
        BikeHandling.DriftResult drift = BikeHandling.DriftStep(_drift, mounted, grounded,
            driftHeld, speed, SteerNow(), dt, h);
        _drift = drift.Next;
        _driftTierNow = _drift.Active ? BikeHandling.DriftTier(_drift.ChargeSec, h) : 0;

        if (drift.Entered)
            _driftEntries++;
        if (drift.ExitTier > 0)
        {
            _driftPeakTier = Mathf.Max(_driftPeakTier, drift.ExitTier);
            _lastBoostTier = drift.ExitTier;
            _lastBoostMps = drift.ExitBoostMps;
            _owedBoostMps += drift.ExitBoostMps;
            // Announced on the harness's own notice strip, where every other bike event is
            // announced, so the missing seam is visible rather than inferred.
            _tuning?.Session.Note(
                $"drift exit tier {drift.ExitTier} worth {drift.ExitBoostMps:F2} m/s — OWED "
              + $"(total {_owedBoostMps:F2}); BikeLayer.RequestImpulse is not on this branch yet");
        }
        _driftPeakTier = Mathf.Max(_driftPeakTier, _driftTierNow);

        // --- turn shaping --------------------------------------------------------------------------
        // NOT during the first agent's --bike-selftest. That run measures BIKE-0/1b/1c's own
        // mechanics against BIKE-0/1b/1c's numbers, and a second writer quietly reshaping
        // RideTurnMul underneath it would make its 23 checks measurements of this packet. This
        // packet's own curve is exercised by --bike-handling-selftest instead.
        if (!_bikeSelfTest)
            ApplyTurnCurve(speed);

        // --- telemetry ------------------------------------------------------------------------------
        _bikeTelemetry?.Sample(new BikeTelemetrySample
        {
            DtSec = dt,
            Position = _avatar.GlobalPosition,
            SpeedMps = speed,
            Mounted = mounted,
            Blend = _bike?.Blend ?? 0f,
            Grounded = grounded,
            LastEvent = _bike?.LastEvent ?? "-",
            DriftEntered = drift.Entered,
            DriftExitTier = drift.ExitTier,
            DriftChargeSec = _drift.ChargeSec,
            CourseName = _courses[_courseIndex].CourseName,
            FootPreset = _preset?.Name ?? "(none)",
            BikePreset = _bikePreset?.Name ?? "(none)",
        });

        UpdateBikeHandlingReadout(speed, mounted);
    }

    /// <summary>
    /// <b>The speed-dependent turn multiplier, written into the ride through the layer's single
    /// writer.</b>
    ///
    /// <para>The bookkeeping is the interesting part. Three things write
    /// <c>BikeTuning.Current.RideTurnMul</c>: an ALT-row preset, the F10 knob panel, and this. The
    /// first two are whole-record writes by a human; this one is a per-tick derived write. So this
    /// remembers exactly what it last wrote, and any difference from that is somebody else's edit,
    /// which is adopted as the new base rather than multiplied on top of. Without that, one drag of
    /// the <c>RideTurnMul</c> slider would compound with the curve every tick and the row would run
    /// away in under a second.</para>
    ///
    /// <para>It writes only when the number actually moves, and only while there is a ride to
    /// re-derive. <c>SetTuning</c> is cheap but not free — it re-derives the whole ride on the next
    /// tick — and a write per tick on foot would be a re-derive per tick of a tuning nobody is
    /// using.</para>
    /// </summary>
    private void ApplyTurnCurve(float speedMps)
    {
        if (_bike is null)
            return;

        BikeTuning b = BikeTuning.Current;
        if (!float.IsNaN(_shapedTurnMulWritten) && b.RideTurnMul != _shapedTurnMulWritten)
            _baseTurnMul = b.RideTurnMul;      // somebody else wrote it; that is the new base
        else if (float.IsNaN(_shapedTurnMulWritten))
            _baseTurnMul = b.RideTurnMul;

        MotorTuning foot = _bike.FootTuning ?? MotorTuning.Current;
        float cap = BikeRig.RideCapMps(foot, b with { RideTurnMul = _baseTurnMul });
        _turnMulNow = BikeHandling.TurnMultiplier(speedMps, cap, BikeHandlingTuning.Current);

        float shaped = _baseTurnMul * _turnMulNow;
        if (_bike.Blend <= 0f)
        {
            // On foot there is no ride to shape. Put the base back so a dismount does not leave the
            // record holding a shaped value that the next mount would then treat as its base.
            if (!float.IsNaN(_shapedTurnMulWritten) && b.RideTurnMul != _baseTurnMul)
            {
                _bike.SetTuning(b with { RideTurnMul = _baseTurnMul });
                _shapedTurnMulWritten = _baseTurnMul;
            }
            return;
        }

        if (b.RideTurnMul == shaped)
            return;
        _bike.SetTuning(b with { RideTurnMul = shaped });
        _shapedTurnMulWritten = shaped;
    }

    /// <summary>
    /// The lateral steering input, -1..1, right positive. Read from the same <c>Input</c> the
    /// shipped intent source reads (or, on a scripted run, from the scripted intent projected onto
    /// the body's right) so the wiggle charge is fed by the stick the player is actually moving
    /// rather than by a second opinion about it.
    /// </summary>
    private float SteerNow()
    {
        if (_scripted is not null)
        {
            Vector3 dir = _scripted.Current.MoveDir;
            Vector3 forward = BikeLayer.FacingOf(_avatar);
            var right = new Vector3(forward.Z, 0f, -forward.X);
            return Mathf.Clamp(dir.Dot(right), -1f, 1f);
        }
        return InputMap.HasAction("move_right") && InputMap.HasAction("move_left")
            ? Input.GetAxis("move_left", "move_right")
            : 0f;
    }

    private void UpdateBikeHandlingReadout(float speedMps, bool mounted)
    {
        if (_bikeHandlingReadout is null)
            return;

        BikeHandlingTuning h = BikeHandlingTuning.Current;
        string driftLine = _drift.Active
            ? $"DRIFT tier {_driftTierNow} charge {_drift.ChargeSec:F2}s "
            + $"({BikeHandling.TierProgress01(_drift.ChargeSec, h) * 100f:F0}% of the next)"
            : $"drift  -   grip {_drift.Grip:F2}   entries {_driftEntries}   peak tier {_driftPeakTier}";

        string owed = _owedBoostMps > 0f
            ? $"OWED {_owedBoostMps:F2} m/s (last: tier {_lastBoostTier}, {_lastBoostMps:F2}) "
            + "— no impulse seam on this branch"
            : "owed  -";

        _bikeHandlingReadout.Text =
            $"HANDLING   lean {_leanDeg,6:F1} deg   yaw {_yawRatePerSec,6:F2} rad/s"
          + $"   turn x{_turnMulNow:F2}\n"
          + $"ROLL DRAWN {_rollDeg,6:F1} deg = lean {_leanDeg,6:F1} + wobble {_wobbleDeg,5:F2}"
          + $"   (fades by {h.WobbleFadeSpeedMps:F1} m/s)\n"
          + $"{driftLine}\n"
          + $"{owed}\n"
          + $"{_bikeCamera?.Line() ?? "CAMERA     (not built)"}\n"
          + $"{_bikeKnobs?.Line() ?? ""}\n"
          + $"{_bikeTelemetry?.SummaryLine() ?? ""}\n"
          + $"slope +{_bike?.SlopeBonusMps ?? 0f:F2} m/s (sin {_bike?.DownhillSin ?? 0f:F2})"
          + $"   {(mounted ? "MOUNTED" : "on foot")} at {speedMps:F2} m/s";
    }
}

/// <summary>
/// <b>The lean, put on the greybox</b> (BIKE-2x, 2026-09-02).
///
/// <para><b>Why this is a node and not two lines in the harness's physics step.</b>
/// <c>BikeGreybox.Track</c> rewrites the greybox root's whole rotation every frame, on the RENDER
/// clock. A roll written on the physics clock would be overwritten by the next render frame roughly
/// half the time, which is not "the lean does not work" — it is a lean that flickers, which is
/// harder to diagnose and reads as a rendering fault. So the roll is written on the same clock, at a
/// higher process priority, from a node whose only job is to run after <c>Track</c> has had its say.
/// The repo already learned this once: see <c>MovementPlayground._Ready</c>'s note on the removed
/// tumble — <i>"a transform another node also writes must be written on EVERY clock that node uses
/// or it flickers."</i></para>
///
/// <para>It reads the yaw the greybox just set and puts the lean back beside it, rather than
/// composing a rotation of its own: <c>Track</c> is the authority on where the bike is pointing and
/// this node is the authority on nothing but its roll.</para>
///
/// <para><b>The drift spark is here for the same reason</b> — it is parented to the greybox, so it
/// has to be built and coloured by something that already runs after the greybox exists. It is the
/// packet's only tier feedback and it is deliberately not a number: colour and brightness, on the
/// bike, in the world.</para>
/// </summary>
public sealed partial class BikeLeanRig : Node3D
{
    /// <summary>Above the harness's own <c>_Process</c> (priority 0), which is where
    /// <c>BikeGreybox.Track</c> is called from.</summary>
    private const int AfterTrackPriority = 300;

    private Node3D? _greybox;
    private System.Func<float>? _leanDeg;
    private System.Func<int>? _tier;
    private System.Func<float>? _tierProgress;
    private MeshInstance3D? _spark;
    private StandardMaterial3D? _sparkMaterial;

    public void Bind(Node3D greybox, System.Func<float> leanDeg, System.Func<int> tier,
        System.Func<float> tierProgress)
    {
        _greybox = greybox;
        _leanDeg = leanDeg;
        _tier = tier;
        _tierProgress = tierProgress;
    }

    public override void _Ready() => ProcessPriority = AfterTrackPriority;

    public override void _Process(double delta)
    {
        if (_greybox is null || _leanDeg is null || !IsInstanceValid(_greybox))
            return;

        // Read the yaw Track just wrote and put the roll beside it. Positive lean = leaning left =
        // a positive rotation about the greybox's local +Z, which tips its +X (right) side up.
        // BikeHandling.LeanSteadyDeg's doc states that convention; this is the only place that
        // depends on it.
        Vector3 rotation = _greybox.Rotation;
        _greybox.Rotation = new Vector3(rotation.X, rotation.Y, Mathf.DegToRad(_leanDeg()));

        UpdateSpark();
    }

    /// <summary>
    /// <b>The tier feedback, and the whole of it.</b> A small emissive quad riding under the bike:
    /// its colour is the tier, its brightness is how far through the tier the charge is, and it is
    /// invisible at tier 0. No counter, no number, no HUD — the packet is explicit that tier
    /// feedback is diegetic only, and a spark that is on the bike in the world is the only channel
    /// that satisfies that.
    /// </summary>
    private void UpdateSpark()
    {
        int tier = _tier?.Invoke() ?? 0;
        if (_spark is null)
        {
            _sparkMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                DisableReceiveShadows = true,
            };
            _spark = new MeshInstance3D
            {
                Name = "DriftSpark",
                Mesh = new QuadMesh { Size = new Vector2(0.55f, 0.55f) },
                MaterialOverride = _sparkMaterial,
                Position = new Vector3(0f, 0.12f, 0.34f),   // at the back wheel, where a skid is
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _greybox!.AddChild(_spark);
        }

        if (tier <= 0)
        {
            _spark.Visible = false;
            return;
        }

        float progress = Mathf.Clamp(_tierProgress?.Invoke() ?? 0f, 0f, 1f);
        Color colour = BikeHandling.TierColour(tier);
        _spark.Visible = true;
        _sparkMaterial!.AlbedoColor = new Color(colour.R, colour.G, colour.B,
            Mathf.Lerp(0.45f, 0.95f, progress));
        _sparkMaterial.Emission = colour;
        _sparkMaterial.EmissionEnergyMultiplier = Mathf.Lerp(1.2f, 4.0f, progress);
        _spark.Scale = Vector3.One * Mathf.Lerp(0.8f, 1.35f, progress);
    }
}

// The `SandboxAvatar.Scripted()` extension that used to live here is DELETED rather than left
// unused. It asked `IIntentSource.IsHumanInput`, which `BikeLayer` does not declare, so it
// answered "scripted" for a human at a keyboard and took the drift with it (see the paragraph at
// the `driftHeld` line). Dead code that encodes a wrong idea is worse than no code: the next
// reader would have found a plausible helper with a convincing doc comment and used it again.
// If a future packet needs "is a person driving this body" in this lab, the honest test is
// `_scripted is null`, which is what the harness itself uses everywhere else.
