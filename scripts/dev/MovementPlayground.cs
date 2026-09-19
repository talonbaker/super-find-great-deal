using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>The MOVE-3 movement playground (MOVE-3d, 2026-08-26).</b> A new, deliberately throwaway scene
/// whose only job is to let the movement be <i>felt</i> — the acceleration ramp, the gears, the
/// skid, coyote time, the jump buffer and the variable-height jump — on real geometry, with the
/// numbers those systems are actually running on printed in the corner.
///
/// <para><b>Everything in here goes through the shipped path.</b> The body is a real
/// <see cref="SandboxAvatar"/> driven by a real <see cref="LocalInputIntentSource"/> through a real
/// <see cref="SandboxCamera"/>, wired the way the offline sandbox wires them, so what a keyboard
/// moves is <c>AvatarMotor.Step</c> and nothing else. The readout is read back out of the live
/// nodes every frame — there is no display copy of any value in this file, because a readout that
/// can disagree with the simulation is worse than no readout at all.</para>
///
/// <para><b>The geometry is three courses, and none of them is this file's.</b>
/// <see cref="CalibrationCourse"/> (MOVE-3e) is the measured half, <see cref="FlowCourse"/>
/// (MOVE-3f) the playable half, and <see cref="ScrambleCourse"/> (MOVE-6) the one you climb. They
/// arrive through the <see cref="MovementCourse"/> contract, which is the whole of what this
/// harness knows about any of them.</para>
///
/// <para><b>Nothing here may reach a course by ordinal (MOVE-5i).</b> TAB cycles, which is what a
/// human wants; every other selection names the course it means and asks
/// <see cref="MovementCourse.IndexOfCourse"/> where it is. The scripted capture used to say
/// <c>_courseIndex + 1</c> — "the other one" — which was unambiguous with two courses and silently
/// retargeted onto the scramble when a third arrived, moving MOVE-4f's calibrated 17.20 m/s landing
/// to 17.69 m/s on a jittered rubble pile without failing anything. A fourth course must not be able
/// to do that again.</para>
///
/// <para><b>Keybinds</b> — printed in the readout too, so nobody needs this doc comment:
/// WASD move, mouse look, Shift sprint, Left Ctrl walk, Space jump (<i>hold</i> for height),
/// TAB teleport to the next course, R respawn at this course, ESC release the mouse.</para>
///
/// <code>
///   powershell -File tests/Run-MovementPlayground.ps1
/// </code>
/// </summary>
public partial class MovementPlayground : Node3D
{
    /// <summary>
    /// <b>The body Talon asked for</b> — whichever one that currently is. MOVE-3g put the classic
    /// greybox here ("bring back the original graybox player character, the one that was before the
    /// gumdrop"); BODY-2 (2026-08-28) points it at <see cref="AvatarVisual.PreferredAvatarKey"/>
    /// instead, which is the box kid today ("The BoxKid.glb is the one I want").
    ///
    /// <para>A constant rather than an environment lookup because this scene has exactly one right
    /// answer for it. If MOVE-3g has not landed, <see cref="AvatarVisual.NormalizeAvatarKey"/> maps
    /// an unknown key onto <see cref="AvatarVisual.PreferredAvatarKey"/> by itself — so the
    /// playground is playable either way, with a different body. Deliberately NOT guarded with a
    /// fallback branch here: a second opinion about what an unknown key means is exactly how two
    /// files come to disagree.</para>
    /// </summary>
    // BODY-1 (2026-08-28): reads the roster's own constant instead of re-spelling it. The
    // duplicate literal was defensible while this key might not exist; it is now
    // AvatarVisual.PreferredAvatarKey and a second spelling is only a way to drift.
    //
    // BODY-2 (2026-08-28) FOLLOWS THE DEFAULT INSTEAD OF NAMING A BODY, and that is the fix rather
    // than a rename. This lab is where the motor's feel is judged, and Talon has now changed the
    // played body twice in a week; a lab pinned to a specific key goes on measuring the gait of a
    // body nobody plays every time he does — silently, and while reading perfectly correctly. The
    // summary above already says the intent is "the body Talon asked for", so it now names that
    // literally and moves when he moves. Naming a SPECIFIC body here is still the right call the
    // day this lab needs to compare two, and on that day it should take a parameter rather than a
    // second constant.
    private const string PlaygroundAvatarKey = AvatarVisual.PreferredAvatarKey;

    /// <summary>How far apart consecutive courses sit on X. The calibration course is about 60 m
    /// wide and the other two footprints are their own authors' business, so this is deliberately
    /// more than any of them needs: two courses that overlap are one broken course, and the extra
    /// distance costs nothing because the only way between them is the teleport key.</summary>
    private const float CourseSpacingM = 160f;

    /// <summary>How far above a course's own spawn point the harness drops a teleported body. Small
    /// — the spawn is meant to be standing on something, and a big drop would make every respawn
    /// open with an airborne readout.</summary>
    private const float SpawnLiftM = 0.4f;

    /// <summary>
    /// <b>Below this, the harness puts you back.</b> A blockout course is a lump of boxes with
    /// nothing under it, so running off the edge is not an edge case — it is the first thing a
    /// player does at sprint, and without this it ends in a fall that never lands. The first
    /// scripted capture run off this harness did exactly that and hung waiting for a touchdown that
    /// could not come.
    ///
    /// <para>Deep enough that it can never fire on a course's own geometry (nothing in a movement
    /// playground is 25 m below its spawn), and it respawns rather than clamping: a body caught
    /// mid-fall and set down is a body whose velocity nobody can explain.</para>
    /// </summary>
    private const float VoidFloorY = -25f;

    private SandboxAvatar _avatar = null!;
    private SandboxCamera _camera = null!;
    /// <summary>BIKE-0: the summonable bike, a layer around the body. Null on a scripted run.</summary>
    private BikeLayer? _bike;
    private BikeGreybox? _bikeMesh;
    /// <summary>BIKE-0: <c>--bike-selftest</c> was passed after the <c>--</c>.</summary>
    private bool _bikeSelfTest;
    private Label _readout = null!;
    private MotorTuningPanel _tuning = null!;

    private readonly List<MovementCourse> _courses = new();
    private int _courseIndex;

    private ScriptedInput? _scripted;
    private string _outDir = "";
    private bool _quitAfter;

    // --- Last-jump measurement ------------------------------------------------------------------
    //
    // Measured in _PhysicsProcess, on the clock the motor itself runs on: a jump arc sampled once
    // per rendered frame would miss its apex by up to a frame and report a height the simulation
    // never produced. Nothing in this block feeds back into the simulation.

    private bool _wasGrounded = true;
    private double _takeoffTimeSec;
    private Vector3 _takeoffPos;
    private float _peakY;
    private bool _airborneFromJump;
    private double _clockSec;

    private float _lastApexM;
    private float _lastDistanceM;
    private float _lastAirtimeSec;
    private bool _haveJump;

    /// <summary>Deepest fall speed of the flight in progress, m/s — the same accumulator
    /// <c>SandboxAvatar</c> keeps, mirrored here so the readout reports the quantity the camera dip
    /// is actually a function of rather than a proxy for it. Reset at every touchdown.</summary>
    private float _maxFallMps;

    /// <summary>Fall speed of the most recent touchdown, m/s. <b>Every</b> touchdown, gated or not
    /// — MOVE-4f's dip has no gate, so a readout that only reported qualifying landings would be
    /// unable to show the one case the design is about (a hop dipping almost nothing).</summary>
    private float _lastLandFallMps = -1f;

    /// <summary>How many arcs have been measured. Only the scripted run reads it, and only to tell
    /// "the jump landed and these numbers are its" from "the numbers are still the previous
    /// jump's" — a distinction a bare <c>_haveJump</c> cannot make.</summary>
    private int _jumpCount;

    /// <summary>How many times the void floor has caught the body. Printed by the scripted run so a
    /// beat that fell off the course is never mistaken for a beat that landed.</summary>
    private int _voidCatches;

    // --- Feel presets (2026-08-28) ------------------------------------------------------------

    /// <summary>The banner naming the loaded feel and what it is asking to be noticed. <b>A
    /// separate label from the readout on purpose</b>: the readout is instruments, read on demand;
    /// this is a heading and has to be legible without being looked for. A preset whose name is
    /// buried in the ninth line of a telemetry block is a preset that gets pressed and then
    /// forgotten about halfway through the note it produced.</summary>
    private Label _presetBanner = null!;

    /// <summary>Which preset was last pressed. <b>Not authoritative</b> — the tuning is — and
    /// moving any knob by hand leaves this naming the preset you departed FROM, which the banner
    /// says outright rather than hides.</summary>
    private MovementPresets.Preset? _preset;
    private BikePresets.Preset? _bikePreset;

    /// <summary>The sprint-default experiment (SHIFT inert, top speed always on). Held so a key can
    /// flip it live; null under <c>-Capture</c>, which drives a scripted intent instead.</summary>
    private SprintDefaultIntentSource? _sprintDefault;

    /// <summary>The unified button grammar (press winds up, release jumps, overhold commits).
    /// Selected BY PRESET rather than by a key of its own — it is half of what a CTRL-bank preset
    /// means, and a preset that named a grammar the input was not running would be the same lie the
    /// banner exists to prevent.</summary>
    private ChargeJumpIntentSource? _chargeJump;

    // --- The skip strip (2026-08-28) ------------------------------------------------------------

    /// <summary>Lead leg of each recent take-off, newest last, as 'L'/'R'. Capped at
    /// <see cref="SkipStripMax"/>.</summary>
    private readonly List<char> _skipStrip = new();

    /// <summary>How many take-offs in a row have alternated their lead leg. THE score: this is the
    /// number Talon is trying to run up, and the one nothing in the game currently shows.</summary>
    private int _skipStreak;

    /// <summary>Seconds between the last two take-offs — the cadence being kept.</summary>
    private float _skipIntervalSec;

    /// <summary>Clock of the previous take-off, for the interval. Negative = none yet.</summary>
    private double _lastTakeoffSec = -1;

    private const int SkipStripMax = 12;

    // --- Slope momentum (2026-08-28) ------------------------------------------------------------

    /// <summary>Live toggle for the slope prototype. Off by default so every earlier preset still
    /// behaves exactly as it did when Talon ruled on it.</summary>
    private bool _slopeMomentum;

    /// <summary>How much of gravity's along-slope component actually reaches the body. 1.0 is the
    /// physically honest value; it is a knob because "honest" and "fun" are different questions and
    /// this lab only exists to answer the second.</summary>
    private float _slopeGain = 1.0f;

    /// <summary>Below this the floor counts as flat. Not zero: a box collider's normal jitters by a
    /// fraction of a degree on contact, and without a dead band a body standing still on the apron
    /// creeps.</summary>
    private const float SlopeDeadZoneDeg = 3f;

    /// <summary>The along-slope acceleration applied last tick, for the readout.</summary>
    private float _slopeAccelNow;

    /// <summary>The floor angle under the body last tick, degrees.</summary>
    private float _slopeAngleNow;

    /// <summary>A keyboard stand-in for <c>-Capture</c>, the same shape <see cref="LocomotionLab"/>
    /// uses: a settable intent with self-clearing edges, so the scripted run drives the production
    /// seam rather than a second movement path.</summary>
    private sealed class ScriptedInput : IIntentSource
    {
        public MoveIntent Current;

        public MoveIntent NextIntent(double delta)
        {
            MoveIntent intent = Current;
            Current = Current with { Jump = false, Interact = false, Throw = false };
            return intent;
        }
    }

    public override void _Ready()
    {
        // The slope probe writes velocity AFTER the avatar's own step, so this node must run last
        // on the physics clock.
        //
        // (A tumble animation lived here too and was removed 2026-08-28 on Talon's ruling — "the
        // rolling does not work, please get rid of it". It rotated the visual root while
        // AvatarVisual posed a crouch on its children, which read as a glitch rather than a roll,
        // and it taught two things worth keeping: a body tumble belongs in AvatarVisual with the
        // legs tucked, and a transform another node also writes must be written on EVERY clock
        // that node uses or it flickers.)
        ProcessPhysicsPriority = 100;
        _outDir = OS.GetEnvironment("SAIL_MOVEPG_OUT");
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--movement-playground-quit")
                _quitAfter = true;
            // BIKE-0: the headless bike self-test (MovementPlayground.BikeSelfTest.cs).
            if (a == "--bike-selftest")
                _bikeSelfTest = true;
        }

        BuildLighting();
        BuildCourses();
        BuildAvatarAndCamera();
        BuildReadout();
        BuildTuningPanel();
        BikeHandlingReady();   // BIKE-2x: the handling model, the bike camera, the knobs, the log

        if (_outDir.Length > 0)
            _ = RunCaptureAsync();
        else if (_bikeSelfTest)
            _ = RunBikeSelfTestAsync();
        // BIKE-4A: the ride channel's A/B evidence run (MovementPlayground.BikeRideCapture.cs).
        // Parses its own flag off the command line, for BikeScriptedFlagPresent's reason.
        else if (BikeRideCaptureReady())
            _ = RunBikeRideCaptureAsync();
    }

    // --- The world ------------------------------------------------------------------------------

    /// <summary>Flat neutral daylight and nothing else. No fog, no night, no atmosphere: every
    /// surface in this scene is grey blockout whose job is to be read at a glance, and a mood pass
    /// over a control surface only makes the control harder to read.</summary>
    private void BuildLighting()
    {
        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-58, -34, 0),
            LightEnergy = 1.05f,
            ShadowEnabled = true,
        });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.60f, 0.67f, 0.74f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.66f, 0.68f, 0.72f),
                AmbientLightEnergy = 0.85f,
            },
        });
    }

    private void BuildCourses()
    {
        var calibration = new CalibrationCourse
        {
            Name = "CalibrationCourse",
            Position = Vector3.Zero,
        };
        var flow = new FlowCourse
        {
            Name = "FlowCourse",
            Position = new Vector3(CourseSpacingM, 0f, 0f),
        };
        // The scramble sits a course-spacing further along again. It is the tall one: a pile you
        // climb rather than a route you run, and the only course here with any vertical scale.
        var scramble = new ScrambleCourse
        {
            Name = "ScrambleCourse",
            Position = new Vector3(CourseSpacingM * 2f, 0f, 0f),
        };

        // The surf park: the first course here with GRADIENTS, and the largest by a wide margin
        // (~200 m). Speed needs room — a surf that ends after two seconds is a ramp, not a run.
        var surf = new SurfCourse
        {
            Name = "SurfCourse",
            Position = new Vector3(CourseSpacingM * 3f, 0f, 0f),
        };

        // BIKE-0 (2026-09-01): the roller run, one continuous descent for the bike prototype.
        var descent = new DescentCourse
        {
            Name = "DescentCourse",
            Position = new Vector3(CourseSpacingM * 4f, 0f, 0f),
        };

        // BIKE-1b (2026-09-01): the bike park, six lanes of one question each. It is 200 m wide,
        // so it sits a spacing beyond the roller run and nothing is placed after it.
        var park = new BikeParkCourse
        {
            Name = "BikeParkCourse",
            Position = new Vector3(CourseSpacingM * 5f, 0f, 0f),
        };

        // BIKE-2x-L (2026-09-02): the zone-6 rolling heightfield, the roster's only continuous
        // surface. Two spacings past the park rather than one — the park is ~200 m wide (lanes at
        // local X -75..+100 around origin 800), so one spacing would land this 90 m patch inside
        // its stairs lane.
        var rolling = new RollingCourse
        {
            Name = "RollingCourse",
            Position = new Vector3(CourseSpacingM * 7f, 0f, 0f),
        };

        AddChild(calibration);
        AddChild(flow);
        AddChild(scramble);
        AddChild(surf);
        AddChild(descent);
        AddChild(park);
        AddChild(rolling);
        _courses.Add(calibration);
        _courses.Add(flow);
        _courses.Add(scramble);
        _courses.Add(surf);
        _courses.Add(descent);
        _courses.Add(park);
        _courses.Add(rolling);

        // Start on the FLOW course, per the packet: the first thing to meet is the half that asks
        // "is there any fun in here", not the half that asks "is the number right".
        _courseIndex = _courses.IndexOf(flow);
    }

    private void BuildAvatarAndCamera()
    {
        _avatar = new SandboxAvatar
        {
            Name = "Avatar",
            // Set BEFORE the node enters the tree: _Ready builds the appearance from whatever this
            // holds at that moment, and a correction afterwards would rebuild a body whose collider
            // had already been measured off the old one.
            AvatarKey = PlaygroundAvatarKey,
            Position = SpawnFor(_courseIndex),
        };
        AddChild(_avatar);

        _camera = new SandboxCamera { Name = "Camera" };
        AddChild(_camera);
        // Attach captures the mouse, which is what arms the whole input path —
        // LocalInputIntentSource returns MoveIntent.None whenever the mouse is free. ESC gives it
        // back (SandboxCamera's own pause handling, left at its default here because this scene is
        // meant to be played rather than photographed).
        _camera.Attach(_avatar);
        _avatar.AimCamera = _camera.CameraNode;
        // The first arrival is an arrival too: the body was placed above rather than teleported, so
        // it never went through TeleportTo and would otherwise open on whatever yaw the camera was
        // constructed with.
        AimAtCourse(_courseIndex);

        if (_outDir.Length > 0)
        {
            _scripted = new ScriptedInput();
            _avatar.IntentSource = _scripted;
            // A scripted run must not depend on where the OS left the pointer.
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (_bikeSelfTest)
        {
            // BIKE-0: the scripted brain drives the SAME bike layer a keyboard does; only the
            // summon press arrives by call instead of by key.
            _scripted = new ScriptedInput();
            _bike = new BikeLayer(_scripted, _avatar) { Scripted = true };
            _avatar.IntentSource = _bike;
            _bikeMesh = new BikeGreybox { Name = "BikeGreybox" };
            AddChild(_bikeMesh);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            // Wrapped, not replaced: the decorator is transparent until its key is pressed, so the
            // default path is still exactly LocalInputIntentSource and the sprint-default question
            // is an A/B on one bit rather than a second input path that could drift from the first.
            // Chained decorators, innermost first: the real local input, then the wind-up
            // grammar (which rewrites the jump edge), then the gear inversion (which rewrites
            // Sprint). Both are transparent until switched on, so the untouched path is still
            // exactly LocalInputIntentSource.
            _chargeJump = new ChargeJumpIntentSource(new LocalInputIntentSource(_camera));
            _sprintDefault = new SprintDefaultIntentSource(_chargeJump);
            // BIKE-0 (2026-09-01): the bike layer wraps everything above. Transparent until Q is
            // pressed; the only thing it does unasked is take the right mouse button, which this
            // lab has no aim pose for.
            _bike = new BikeLayer(_sprintDefault, _avatar);
            _avatar.IntentSource = _bike;

            _bikeMesh = new BikeGreybox { Name = "BikeGreybox" };
            AddChild(_bikeMesh);
        }
    }

    private void BuildReadout()
    {
        var layer = new CanvasLayer { Name = "Readout" };
        AddChild(layer);
        _readout = new Label { Position = new Vector2(18, 14) };
        _readout.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _readout.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _readout.AddThemeConstantOverride("outline_size", 6);
        _readout.AddThemeFontSizeOverride("font_size", 18);
        layer.AddChild(_readout);

        // The preset banner, anchored to the BOTTOM so it cannot collide with the readout however
        // many lines that grows to. Bottom-left rather than centred because the eye is already
        // there for the readout, and a heading you have to hunt for is a heading that goes unread.
        _presetBanner = new Label { Name = "PresetBanner" };
        layer.AddChild(_presetBanner);
        _presetBanner.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _presetBanner.OffsetLeft = 18;
        _presetBanner.OffsetRight = 940;
        _presetBanner.OffsetTop = -215;
        _presetBanner.OffsetBottom = -16;
        _presetBanner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _presetBanner.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.55f));
        _presetBanner.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _presetBanner.AddThemeConstantOverride("outline_size", 6);
        _presetBanner.AddThemeFontSizeOverride("font_size", 19);
    }

    // --- Feel presets ---------------------------------------------------------------------------

    /// <summary>
    /// <b>Load a whole named tuning in one keystroke.</b> Goes through
    /// <see cref="MotorTuningSession.Apply"/> — the same one writer the sliders use — so a preset
    /// is validated, clamped, warned about and written to the tuning file exactly as a hand edit
    /// would be. <b>There is deliberately no second write path for presets</b>: one that bypassed
    /// the validator could seat a tuning the panel considers illegal and then show it as legal.
    ///
    /// <para><b>A refusal is reported and does not move <see cref="_preset"/>.</b> The failure this
    /// guards against is the quiet one — a preset that is refused, leaves the previous tuning in
    /// force, and still renames the banner, so the next twenty minutes of notes are filed against
    /// a feel that was never loaded.</para>
    /// </summary>
    private void ApplyPreset(MovementPresets.Preset preset)
    {
        if (_tuning is null)
            return;
        if (_tuning.Session.Apply(preset.Tuning))
        {
            _preset = preset;
            // A foot preset pressed while riding: the bike re-derives its ride from the new foot
            // and restores to it on dismount, rather than to the preset the bike came out on.
            _bike?.ReplaceFoot(preset.Tuning);
            // The input scheme is half of what a CTRL preset means; applying the tuning without it
            // would seat a grammar the banner does not name.
            if (_chargeJump is not null)
                _chargeJump.Enabled = preset.ChargeJump;
            _tuning.Session.Note(
                $"preset {MovementPresets.KeyLabel(preset)} - {preset.Name}");
        }
        else
        {
            _tuning.Session.Note(
                $"preset {MovementPresets.KeyLabel(preset)} ({preset.Name}) REFUSED - "
              + _tuning.Session.LastRefusal);
        }
    }

    /// <summary>An ALT-row bike preset (BIKE-1b). The bike's numbers are not a session row, so
    /// there is nothing to refuse; the layer re-derives the ride on its next tick if one is on.</summary>
    private void ApplyBikePreset(BikePresets.Preset preset)
    {
        if (_bike is not null)
            _bike.SetTuning(preset.Tuning);
        else
            BikeTuning.Current = preset.Tuning;
        _bikePreset = preset;
        _tuning?.Session.Note($"bike preset {BikePresets.KeyLabel(preset)} - {preset.Name}");
    }

    /// <summary>The digit a key event names, or -1. Number row and keypad both, because a preset
    /// you cannot reach from the pad you happen to be resting on is one you will not press.</summary>
    private static int DigitOf(Key key) => key switch
    {
        >= Key.Key0 and <= Key.Key9 => (int)(key - Key.Key0),
        >= Key.Kp0 and <= Key.Kp9 => (int)(key - Key.Kp0),
        _ => -1,
    };

    /// <summary>
    /// <b>The knobs (MOVE-4d, extended by MOVE-5d).</b> A slider for every one of the
    /// <see cref="MotorTuningKnobs.All"/> tuning fields — thirty-one when MOVE-4f closed, and grown
    /// by the crouch, chain and air-jump rows since, so the live count is
    /// <see cref="MotorTuningKnobs.All"/><c>.Count</c> and not a figure typed here — the pin that
    /// constrains each one named on the row, the coupled invariants live, and the save / reload /
    /// print / reset keys that make a tuning survive the window closing.
    ///
    /// <para><b>It takes neither the camera nor the controls</b> — the standing law is that UI may
    /// take the screen only when it has taken both. It is a right-hand column; WASD, the mouse and
    /// SPACE keep working with it open, and every knob is reachable from the arrow keys, because
    /// tuning a jump you cannot perform while looking at the slider is not tuning.</para>
    ///
    /// <para><b>Under <c>-Capture</c> the panel neither loads nor writes the tuning file, and starts
    /// hidden.</b> The scripted run's whole value is that its apex and airtime figures can be held
    /// against MOVE-3b's — which they cannot be if a saved tuning is silently in force — and a
    /// measurement run must never overwrite a tuning a human left in that file. The capture takes
    /// its own shot of the panel at the end (<c>07-tuning-panel</c>).</para>
    /// </summary>
    private void BuildTuningPanel()
    {
        // BIKE-2x-L: the two bike scripted flags are parsed in BikeHandlingReady, which runs after
        // this — so they are read off the command line here. A scripted measurement run with
        // FileIo on is exactly the silently-in-force-tuning failure the -Capture note above
        // records: the first headed bike capture ran with Talon's live movement-tuning.json partly
        // applied (TurnLerp 5 in force against the shipped 12) and the panel open over half the
        // frame.
        bool scripted = _outDir.Length > 0 || _bikeSelfTest || BikeScriptedFlagPresent()
            || BikeRideCaptureFlagPresent();   // BIKE-4A
        _tuning = new MotorTuningPanel
        {
            Name = "TuningPanel",
            FileIo = !scripted,
            StartVisible = !scripted,
        };
        AddChild(_tuning);
        // BIKE-0: the bike announces mounts, dismounts and any refused tuning write on the same
        // notice strip the presets and the slope toggle use.
        if (_bike is not null)
            _bike.Notice = line => _tuning.Session.Note(line);
    }

    // --- Spawning and teleporting ---------------------------------------------------------------

    /// <summary>World-space spawn for a course: the course's own local spawn point, offset by the
    /// course's origin and lifted clear of its floor. The lift is the harness's, not the course's —
    /// a course author states where their floor is, not how far a teleport should drop.</summary>
    private Vector3 SpawnFor(int index) =>
        _courses[index].Position + _courses[index].SpawnPointLocal + Vector3.Up * SpawnLiftM;

    /// <summary>
    /// <b>Where a course is, by identity (MOVE-5i).</b> The one way anything in this file other than
    /// TAB is allowed to name a course: a type, resolved against the live roster, loud if it is not
    /// there. See <see cref="MovementCourse.IndexOfCourse"/> for the ordinal this replaced and what
    /// it silently did to MOVE-4f's calibration when a third course arrived.
    /// </summary>
    private int CourseIndexOf<TCourse>() where TCourse : MovementCourse
    {
        var types = new List<System.Type>(_courses.Count);
        foreach (MovementCourse course in _courses)
            types.Add(course.GetType());
        return MovementCourse.IndexOfCourse(types, typeof(TCourse));
    }

    /// <summary>
    /// <b>Point the camera at whatever the course says is worth looking at (MOVE-5i).</b> Called on
    /// every arrival — the first spawn and every teleport and respawn — because "put me back" should
    /// put the view back too, not just the body.
    ///
    /// <para>A course with no <see cref="MovementCourse.SpawnLookAtLocal"/> is left strictly alone,
    /// so an arrival frame this packet did not set out to change is byte-identical to before. The
    /// yaw goes through <c>SandboxCamera.SetOrbit</c>, the shipped hook for placing the orbit
    /// directly, and the pitch is handed back unchanged: this is a heading, and stealing the
    /// player's pitch on every respawn would be a second opinion nobody asked for.</para>
    ///
    /// <para><b>It does not touch the body's own rotation.</b> The motor owns that and turns it to
    /// face movement; a harness writing a second facing into it is the shipped defect this repo
    /// already paid for once (a chase entity facing backward while pursuing).</para>
    /// </summary>
    private void AimAtCourse(int index)
    {
        if (_camera is null)
            return;
        MovementCourse course = _courses[index];
        if (course.SpawnLookAtLocal is not { } lookAt)
            return;
        _camera.SetOrbit(MovementCourse.YawTowards(course.SpawnPointLocal, lookAt), _camera.Pitch);
    }

    /// <summary>
    /// Put the body at a course's spawn and stop it dead.
    ///
    /// <para><b>Through <see cref="SandboxAvatar.ServerTeleportTo"/>, never by writing
    /// <c>GlobalPosition</c>:</b> that is the shipped seam, and it bumps the teleport epoch so the
    /// camera cuts rather than sweeping the whole 160 m between courses with its spring arm still
    /// attached. Zeroing the velocity afterwards is this harness's own call — a teleport that
    /// carried 8 m/s of sprint into the destination would drop the player off the far side of it,
    /// and a playground you cannot get unstuck in wastes the session it was built for.</para>
    /// </summary>
    private void TeleportTo(int index)
    {
        _courseIndex = Mathf.PosMod(index, _courses.Count);
        _avatar.ServerTeleportTo(SpawnFor(_courseIndex));
        _avatar.Velocity = Vector3.Zero;
        AimAtCourse(_courseIndex);
        // The measurement below is about a jump, and a teleport is not one. Re-arming here is what
        // stops the next landing reporting a 160 m "jump distance".
        _airborneFromJump = false;
        _wasGrounded = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Echo is dropped everywhere except the panel's six navigation keys, which want key
        // repeat: holding RIGHT to walk a knob up its range is the whole ergonomics of a slider
        // driven from the keyboard. A repeating F1 or F7 would be a toggle nobody could aim.
        if (@event is InputEventKey { Pressed: true, Echo: true } repeat)
        {
            if (_tuning is not null && IsRepeatable(repeat.Keycode) && _tuning.HandleKey(repeat))
                GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        // The panel first: it owns F1/F2/F4/F5/F7, the arrows, PGUP/PGDN and HOME, and none of
        // those are the harness's. It never claims TAB, R or ESC.
        if (_tuning is not null && _tuning.HandleKey(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (key.Keycode)
        {
            case Key.Tab:
                TeleportTo(_courseIndex + 1);
                GetViewport().SetInputAsHandled();
                break;
            case Key.R:
                TeleportTo(_courseIndex);
                GetViewport().SetInputAsHandled();
                break;

            // G — the sprint-default experiment. A toggle rather than a launch flag because the
            // only useful form of this question is "these two, on this piece of ground, a second
            // apart"; relaunching between the halves of an A/B loses the comparison in the load.
            // K — the slope prototype. A toggle rather than a preset row because it is not a
            // tuning at all; it is a term this harness adds to the body from outside.
            case Key.K:
                _slopeMomentum = !_slopeMomentum;
                _tuning?.Session.Note(_slopeMomentum
                    ? "slope momentum ON — downhill accelerates (harness probe, not the motor)"
                    : "slope momentum OFF");
                GetViewport().SetInputAsHandled();
                break;

            case Key.G when _sprintDefault is not null:
                _sprintDefault.Enabled = !_sprintDefault.Enabled;
                _tuning?.Session.Note(_sprintDefault.Enabled
                    ? "sprint default ON — SHIFT is inert, L-CTRL is the brake"
                    : "sprint default OFF — SHIFT sprints, as shipped");
                GetViewport().SetInputAsHandled();
                break;

            default:
            {
                // The modifier selects the bank, read off the EVENT rather than polled, so a
                // bank cannot be chosen by a modifier released between the press and this frame.
                // CTRL wins over SHIFT when both are down: the CTRL bank is the grammar bank and
                // the more specific request.
                int digit = DigitOf(key.Keycode);
                // ALT and a digit is the bike row (BIKE-1b); it composes with the foot rows rather
                // than competing with them, so it is checked first and never falls through.
                if (digit >= 0 && key.AltPressed && !key.CtrlPressed)
                {
                    if (BikePresets.ForKey(digit) is { } bikePreset)
                    {
                        ApplyBikePreset(bikePreset);
                        GetViewport().SetInputAsHandled();
                    }
                    break;
                }
                PresetBank bank = key.CtrlPressed ? PresetBank.Ctrl
                    : key.ShiftPressed ? PresetBank.Shift
                    : PresetBank.Plain;
                if (digit >= 0 && MovementPresets.ForKey(digit, bank) is { } preset)
                {
                    ApplyPreset(preset);
                    GetViewport().SetInputAsHandled();
                }
                break;
            }
        }
    }

    /// <summary>The panel keys that may auto-repeat while held: move the selection, move the knob.
    /// Nothing that saves, prints, resets or toggles.</summary>
    private static bool IsRepeatable(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Pageup or Key.Pagedown;

    // --- Measurement ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _clockSec += delta;

        // The void floor first, so nothing below measures an arc that ended in a fall.
        if (_avatar.GlobalPosition.Y < VoidFloorY)
        {
            _voidCatches++;
            TeleportTo(_courseIndex);
            return;
        }

        bool grounded = _avatar.Grounded;
        Vector3 pos = _avatar.GlobalPosition;

        if (_wasGrounded && !grounded)
        {
            // A rising body left the ground under its own power; a falling one walked off a ledge.
            // Only the first is a jump, and only a jump has an apex worth reporting.
            _airborneFromJump = _avatar.Velocity.Y > 0f;
            _takeoffTimeSec = _clockSec;
            _takeoffPos = pos;
            _peakY = pos.Y;
            // The skip strip records EVERY take-off, including a walk-off-a-ledge, because the
            // lead leg is latched on any airborne transition and a strip that silently skipped
            // some of them would show a rhythm the body did not have.
            RecordSkipBeat();
        }
        else if (!_wasGrounded && grounded && _airborneFromJump)
        {
            _lastApexM = _peakY - _takeoffPos.Y;
            _lastDistanceM = new Vector2(pos.X - _takeoffPos.X, pos.Z - _takeoffPos.Z).Length();
            _lastAirtimeSec = (float)(_clockSec - _takeoffTimeSec);
            _haveJump = true;
            _jumpCount++;
            _airborneFromJump = false;
        }
        else if (!grounded && _airborneFromJump)
        {
            _peakY = Mathf.Max(_peakY, pos.Y);
        }

        // The landing channel, tracked separately from the jump arc above because it is about
        // EVERY touchdown — a step off a kerb has no apex and still lands. Same accumulate-then-
        // read-at-touchdown shape SandboxAvatar uses, so the two report the same quantity.
        if (!grounded)
            _maxFallMps = Mathf.Max(_maxFallMps, -_avatar.Velocity.Y);
        else if (!_wasGrounded)
        {
            _lastLandFallMps = _maxFallMps;
            _maxFallMps = 0f;
        }

        _wasGrounded = grounded;
        // Physics clock ONLY: it writes velocity, and velocity written on the render clock is a
        // frame-rate-dependent force. ProcessPhysicsPriority puts it after the avatar's own step.
        UpdateSlopeMomentum(delta);
        // BIKE-0: the bike's post-step writes (bursts, skid, the landing clamp) go after the slope
        // term, on the same clock and for the same reason: the body has already stepped.
        _bike?.PostStep(delta);
        // BIKE-4A: the RIDER's body, read off the settled layer state. Reads only — see
        // MovementPlayground.BikeRide.cs.
        BikeRideVisuals((float)delta);
        BikeHandlingPhysics(delta);   // BIKE-2x: last, so every value it reads is this tick's settled one
    }

    /// <summary>Whether the jump key is held right now. Polled from the same <c>Input</c> the
    /// shipped intent source polls (or, under <c>-Capture</c>, from the scripted intent), so the
    /// readout's answer and the motor's answer come from one source.</summary>
    private bool JumpHeldNow() =>
        _scripted?.Current.JumpHeld ?? Input.IsActionPressed("jump");

    /// <summary>Is the variable-jump gravity cut applying this instant? Asked of
    /// <see cref="AvatarMotor.GravityFor"/> itself rather than re-derived here — the readout must
    /// never be able to claim a cut the motor is not applying.</summary>
    private bool JumpCutActive()
    {
        if (_avatar.Grounded || _avatar.Velocity.Y <= 0f)
            return false;
        float g = AvatarMotor.GravityFor(_avatar.Velocity.Y, JumpHeldNow(), locked: false);
        return g > AvatarMotor.Gravity * 1.001f;
    }

    public override void _Process(double delta)
    {
        UpdateReadout();
        if (_bikeMesh is not null && _bike is not null)
            _bikeMesh.Track(_bike, _avatar, (float)delta);
    }

    /// <summary>
    /// One of the two forgiveness timers, for the readout.
    ///
    /// <para><b>An expired timer reads "expired", not "-6.783 s".</b> Both timers run down past
    /// zero without a floor and <c>AvatarMotor.StepJump</c> treats anything at or below zero as
    /// spent, so the negative tail is arithmetic rather than state - and a readout showing seven
    /// seconds of it buries the 0.12 s window that is the only interesting thing either timer ever
    /// does. <b>Every live value is still printed exactly</b>; only the already-expired case is
    /// collapsed, so this cannot hide a timer that is running.</para>
    /// </summary>
    private static string Timer(float seconds) =>
        seconds > 0f ? $"{seconds,6:F3} s" : "     -  ";

    private void UpdateReadout()
    {
        float speed = new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();
        string jump = _haveJump
            ? $"apex {_lastApexM,5:F3} m   distance {_lastDistanceM,5:F3} m"
              + $"   airtime {_lastAirtimeSec,5:F3} s"
            : "(none yet - press SPACE)";

        _readout.Text =
            $"COURSE     {_courses[_courseIndex].CourseName}\n"
            + $"speed      {speed,6:F2} m/s   gear {_avatar.Visual.Gear}\n"
            + $"state      {(_avatar.Grounded ? "GROUNDED" : "AIRBORNE")}"
            + $"   vY {_avatar.Velocity.Y,6:F2} m/s"
            + $"   skid {(_avatar.SkiddingNow ? $"YES {_avatar.SkidRemainingSec:F2}s" : "no")}\n"
            + $"{MovementVerbReadout.Lines(MotorTuning.Current, VerbStateNow())}\n"
            + $"coyote     {Timer(_avatar.CoyoteRemainingSec)}"
            + $"   buffer {Timer(_avatar.JumpBufferRemainingSec)}"
            + $"   jump cut {(JumpCutActive() ? "ACTIVE" : "-")}\n"
            + $"last jump  {jump}\n"
            + $"{LandingLine()}\n"
            + $"{SpaceGrammarLine()}\n"
            + $"{SlopeLine()}\n"
            + $"{SkipLine()}\n"
            + $"{GearLine()}\n"
            + $"{(_bike is null ? "BIKE       (scripted)" : _bike.Line())}\n"
            + "\n"
            + "WASD move   mouse look   SHIFT sprint   L-CTRL walk\n"
            + $"SPACE jump (HOLD for height)   TAB next course ({_courses.Count})   R respawn"
            + "   ESC free the mouse\n"
            + "F1 knob panel   arrows select/nudge   F2 save   F4 reload   F5 print C#   F7 reset\n"
            + "0-9 PRESETS   SHIFT+0-9 BLENDS/SPEED/SKIP   CTRL+0-1 SURF   ALT+0-9 BIKE PRESETS"
            + "   K slope momentum   G sprint-default\n"
            + "Q bike out/away (in the AIR = burst; as you LAND = burst; rolling = stumble)"
            + "   RMB slide / DRIFT   H hold-to-sprint ramp   N stumble on/off";

        UpdatePresetBanner();
    }

    /// <summary>
    /// <b>What the SPACE key is doing RIGHT NOW, in words.</b>
    ///
    /// <para><b>Why words and not another number.</b> The crouch verbs are the one part of this
    /// motor whose behaviour is a GRAMMAR - the same key means jump, or height, or crouch, or
    /// nothing, depending on a clock and a speed - and a grammar is the thing a numeric readout is
    /// worst at teaching. The verb line above prints the state the BODY is in; this prints the
    /// state the BUTTON is in and what it is about to do next, which is the half a player cannot
    /// see and cannot infer.</para>
    ///
    /// <para><b>Every branch is derived from the live tuning and the live body</b>, and the
    /// threshold it quotes is <c>SlideEnterSpeedMps</c> read off the tuning rather than a figure
    /// typed here, so it stays true when the slider moves.</para>
    /// </summary>
    private string SpaceGrammarLine()
    {
        MotorTuning t = MotorTuning.Current;
        bool held = JumpHeldNow();
        float speed = new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();

        if (!_avatar.Grounded)
            return held
                ? "SPACE      HELD, airborne  ->  full height; keep holding THROUGH the landing "
                + "and you will CROUCH, not jump"
                : "SPACE      up, airborne  ->  the release cut is applying: this is the short "
                + "jump";

        if (!held)
            return "SPACE      up, grounded  ->  press to jump";

        MoveVerb verb = _avatar.VerbNow;
        if (verb != MoveVerb.Normal)
            return $"SPACE      HELD in {MovementVerbReadout.VerbName(verb)}  ->  RELEASE"
                 + " to stand up. A fresh PRESS is the only way back to a jump";

        int clock = _avatar.VerbClockTicks == AvatarMotor.VerbClockAirborne
            ? 0 : _avatar.VerbClockTicks;
        int window = t.JumpHoldWindowTicks;
        string next = AvatarMotor.EntryVerbFor(t, speed) == MoveVerb.Slide ? "SLIDE" : "TUCK";
        return $"SPACE      HELD {clock,2}/{window} ticks  ->  {next} at {t.JumpHoldWindowSec:F2} s"
             + $"   (you: {speed:F1} m/s, the slide line: {t.SlideEnterSpeedMps:F1} m/s)";
    }

    /// <summary>
    /// <b>One take-off, recorded for the skip strip.</b>
    ///
    /// <para><b>What it reads and why that is legal.</b> The lead leg
    /// (<see cref="AvatarVisual.LeadLegIsLeft"/>) and the clock. <b>It does not touch the chain</b>
    /// - not its depth, not its timer, not any accessor by which the chain can be reached - because
    /// spec &#167;6.6 forbids a chain readout and <c>ChainNoUiAuditTests</c> sweeps every lab and UI
    /// source for exactly that. The two are easy to confuse and must not be: the chain is a REWARD
    /// (a raised wish speed), the alternation is a GAIT (which leg leads).</para>
    ///
    /// <para><b>Why the lead leg is worth showing at all.</b> <c>AvatarVisual</c> latches it once
    /// per flight as <c>_leadLegLeft = _gaitPhase &gt;= 0.5f</c>, and the gait phase advances with
    /// distance travelled - so whether two consecutive hops alternate is decided by the phase
    /// relationship between hop cadence and stride length. It is a real, learnable skill the game
    /// gives no feedback on whatsoever. This is that missing channel and nothing more: it reports
    /// what the body did, and changes nothing about what it does.</para>
    /// </summary>
    private void RecordSkipBeat()
    {
        char leg = _avatar.Visual.LeadLegIsLeft ? 'L' : 'R';

        // Alternation is a property of a PAIR, so the first beat of a session can neither continue
        // a streak nor break one - it starts one.
        if (_skipStrip.Count > 0)
            _skipStreak = _skipStrip[^1] != leg ? _skipStreak + 1 : 0;

        _skipStrip.Add(leg);
        if (_skipStrip.Count > SkipStripMax)
            _skipStrip.RemoveAt(0);

        _skipIntervalSec = _lastTakeoffSec >= 0 ? (float)(_clockSec - _lastTakeoffSec) : 0f;
        _lastTakeoffSec = _clockSec;
    }

    /// <summary>
    /// <b>The skip strip.</b> Every recent take-off's lead leg, the alternating streak, and the
    /// cadence that produced it. The strip is printed rather than summarised because a streak
    /// counter alone says <i>that</i> you broke it and the strip says <i>where</i>.
    /// </summary>
    private string SkipLine()
    {
        if (_skipStrip.Count == 0)
            return "SKIP       (no hops yet - hop continuously and watch this line)";

        string strip = string.Join(" ", _skipStrip);
        string cadence = _skipIntervalSec > 0f ? $"{_skipIntervalSec:F2} s" : "  -  ";
        string verdict = _skipStreak >= 2
            ? $"ALTERNATING x{_skipStreak}"
            : _skipStrip.Count >= 2 && _skipStrip[^1] == _skipStrip[^2]
                ? "BROKE - same leg twice"
                : "-";
        return $"SKIP       {strip,-24}  streak {_skipStreak,2}  cadence {cadence}  {verdict}";
    }

    /// <summary>
    /// <b>Slope momentum, prototyped from outside the motor</b> (2026-08-28, Talon's session).
    ///
    /// <para><b>The gap this fills.</b> <c>AvatarMotor</c> reads the floor normal NOWHERE — the
    /// motor is flat-world, so running down a hill gives exactly what running along a plain gives.
    /// Every reference Talon named for the feel he wants (Jak's hoverboard, BOTW shield surfing,
    /// the TOTK bikes) is fundamentally about gradients, so none of them is testable here without
    /// this. It is the prerequisite, not an enhancement.</para>
    ///
    /// <para><b>Why it can live outside the motor at all.</b> The motor accelerates velocity TOWARD
    /// a wish at a rate; it does not clamp velocity to it. So a body pushed above its wish is not
    /// snapped back — it decelerates at <c>Deceleration</c>, which is exactly the shape of friction.
    /// This method adds gravity's along-slope component after the motor has run, and the motor's
    /// own deceleration is what bleeds it off again. Downhill you gain when the slope beats the
    /// friction; uphill you lose to both at once. <b>Which makes <c>Deceleration</c> the single
    /// most important knob in a surf preset</b>: at the shipped 21 m/s² no reachable gradient can
    /// out-accelerate it, and the whole effect is invisible. The SURF presets drop it near its 2.0
    /// floor for precisely this reason.</para>
    ///
    /// <para><b>It runs at ProcessPhysicsPriority 100, after the avatar</b>, so the write lands on
    /// the velocity the motor will read next tick — a one-tick lag that is invisible at 60 Hz and
    /// the only honest way to add a term to a body whose own code you are not editing.</para>
    ///
    /// <para><b>THIS IS A PROBE AND MUST NOT SHIP.</b> Real slope momentum belongs inside
    /// <c>AvatarMotor.Step</c>, where it would be deterministic, replicated, and available to the
    /// skid and the slide as well. Here it is a harness term bolted to the outside of a body that
    /// does not know about it, so it cannot survive a reconciliation and it is invisible to every
    /// test in the suite. It exists to answer "is surfing fun here" before anyone pays for the real
    /// version — the same bargain the tumble above is on.</para>
    /// </summary>
    private void UpdateSlopeMomentum(double delta)
    {
        _slopeAccelNow = 0f;
        _slopeAngleNow = 0f;
        // BIKE-0: a MOUNTED body carries its slope speed inside the motor's own wish (see
        // BikeTuning.RideSlopeGain for the measurement that made it so); the K probe below is the
        // body on foot's, exactly as before, and never stacks on the bike's.
        bool mounted = _bike?.Mounted ?? false;
        if (!_slopeMomentum || mounted || _avatar is null || !_avatar.IsOnFloor())
            return;
        float gain = _slopeGain;

        Vector3 normal = _avatar.GetFloorNormal();
        if (!normal.IsFinite() || normal.LengthSquared() < 0.5f)
            return;

        float angleRad = normal.AngleTo(Vector3.Up);
        _slopeAngleNow = Mathf.RadToDeg(angleRad);
        if (_slopeAngleNow < SlopeDeadZoneDeg)
            return;

        // Downhill is gravity projected onto the floor plane. Normalised rather than scaled by the
        // projection's own length, because that length IS sin(angle) and using it twice would make
        // the acceleration fall off as the square of the gradient.
        Vector3 downhill = (Vector3.Down - normal * Vector3.Down.Dot(normal));
        if (downhill.LengthSquared() < 1e-6f)
            return;
        downhill = downhill.Normalized();

        _slopeAccelNow = AvatarMotor.Gravity * Mathf.Sin(angleRad) * gain;
        _avatar.Velocity += downhill * _slopeAccelNow * (float)delta;
    }

    /// <summary>The slope prototype's state, on the readout — including the friction it is fighting,
    /// because a gain that loses to <c>Deceleration</c> looks exactly like a gain that is switched
    /// off.</summary>
    private string SlopeLine()
    {
        if (!_slopeMomentum)
            return "SLOPE      off   [K to switch on]";
        float friction = MotorTuning.Current.Deceleration;
        string verdict = _slopeAngleNow < SlopeDeadZoneDeg
            ? "flat"
            : _slopeAccelNow > friction ? "GAINING" : $"losing to friction ({friction:F1})";
        return $"SLOPE      on   {_slopeAngleNow,4:F1}°   pull {_slopeAccelNow,5:F2} m/s²"
             + $"   vs friction {friction,4:F1}   {verdict}   [K]";
    }

    /// <summary>The gear experiment's state, always shown — an input mode you cannot see is
    /// an input mode you will blame the tuning for.</summary>
    private string GearLine() =>
        _sprintDefault is null
            ? "GEAR       (scripted)"
            : _sprintDefault.Enabled
                ? "GEAR       SPRINT IS DEFAULT - SHIFT inert, L-CTRL brakes   [G flips]"
                : "GEAR       shipped - SHIFT sprints, L-CTRL walks            [G flips]";

    /// <summary>
    /// <b>The banner: which feel is loaded, and what it is asking to be noticed.</b>
    ///
    /// <para><b>It reports hand edits rather than hiding them.</b> <see cref="MotorTuning"/>
    /// is a record struct, so an exact value comparison against the preset's own tuning is
    /// the whole test — and the moment a slider moves the banner says so, instead of going on
    /// claiming a preset that is no longer loaded. That claim is the one thing this banner
    /// could plausibly get wrong, and it is the one that would quietly mis-file every note
    /// taken after it.</para>
    /// </summary>
    private void UpdatePresetBanner()
    {
        if (_preset is not { } p)
        {
            _presetBanner.Text =
                "PRESS 0-9 FOR A FEEL PRESET, OR SHIFT+1-4 FOR THE BLENDS - each loads a "
              + "whole named tuning and says "
              + "what you should be feeling.\n"
              + "0 is the shipped baseline. Press it BETWEEN the others - the comparison "
              + "IS the measurement."
              + BikeBannerLines();
            return;
        }

        // While riding, the live tuning is the RIDE derived from this preset, honestly; the
        // comparison is against the foot tuning the bike captured, not against the live rows.
        MotorTuning live = _bike?.FootTuning ?? _tuning?.Session.Live ?? p.Tuning;
        bool edited = _tuning is not null && !live.Equals(p.Tuning);
        string stale = edited ? "   (+ HAND EDITS - no longer this preset)"
            : _bike?.FootTuning is not null ? "   (riding: the ride is derived from it)" : "";
        _presetBanner.Text =
            $"PRESET {MovementPresets.KeyLabel(p)} - {p.Name}{stale}\n"
          + $"feel:  {p.Feel}\n"
          + $"try:   {p.Try}"
          + BikeBannerLines();
    }

    /// <summary>The bike half of the banner (BIKE-1b): which ALT-row preset the bike is on, if any,
    /// and its two sentences. Appended under the foot preset so the two axes read as one.</summary>
    private string BikeBannerLines()
    {
        if (_bikePreset is not { } bp)
            return "\nBIKE   ALT+0-9 for a bike preset - each is a whole bike over WHATEVER foot preset is loaded.";
        bool edited = !BikeTuning.Current.Equals(bp.Tuning with { StumbleEnabled = BikeTuning.Current.StumbleEnabled });
        string stale = edited ? "   (+ EDITS)" : "";
        string stumble = BikeTuning.Current.StumbleEnabled != bp.Tuning.StumbleEnabled
            ? $"   (N: stumble {(BikeTuning.Current.StumbleEnabled ? "ON" : "OFF")})" : "";
        return $"\nBIKE {BikePresets.KeyLabel(bp)} - {bp.Name}{stale}{stumble}\n"
             + $"feel:  {bp.Feel}\n"
             + $"try:   {bp.Try}";
    }

    /// <summary>
    /// <b>MOVE-5's verb state, gathered off the live body every frame</b> for
    /// <see cref="MovementVerbReadout"/> to render.
    ///
    /// <para><b>Assembled, not remembered.</b> Every field is read out of a
    /// <see cref="SandboxAvatar"/> accessor at the moment the readout is composed, so this cannot
    /// become a display copy that outlives the tick it was taken from — the same rule the rest of
    /// this file's readout follows. <c>SandboxAvatar</c> exposes these values individually rather
    /// than the whole <see cref="MoveState"/>, so this is where they are put back together.</para>
    ///
    /// <para><b>The chain is not gathered at all</b> (spec §6.6, enforced by MOVE-5g). MOVE-5d
    /// assembled the depth and its grace timer here so the air line could ask
    /// <see cref="AvatarMotor.MomentumGranted"/> for one bit — whether the airborne speed ceiling
    /// was being held off — and that assembly was the single hit
    /// <c>ChainNoUiAuditTests.NoUiOrLabSourceCanReachTheChain</c> found when it swept every UI and
    /// lab source for any identifier by which the chain can be reached. The bit is gone with it: the
    /// two chain fields stay at their defaults, no accessor on the body is touched, and the chain's
    /// only channel is MOVE-5c's posture. Do not re-add them for a readout, however small — the
    /// sweep is what makes that promise checkable rather than remembered.</para>
    /// </summary>
    private MoveState VerbStateNow() => new()
    {
        Verb = _avatar.VerbNow,
        VerbClockTicks = _avatar.VerbClockTicks,
        AirJumpsUsed = _avatar.AirJumpsUsed,
    };

    /// <summary>
    /// <b>The landing channel, in metres and to four decimals</b> (MOVE-4f). The dip is the one
    /// knob in this wave that can only be judged by a human with the game running, and it was
    /// unwired until MOVE-4f, so the harness now shows its inputs and its output rather than
    /// leaving a slider to be trusted: the fall speed of the last touchdown, the depth that
    /// landing armed, and what the camera is applying this instant.
    ///
    /// <para><b>Four decimals on purpose.</b> Choice C's whole claim is that a jog tap's dip is
    /// imperceptible by construction — a number that rounds to 0.00 m at two decimals cannot show
    /// the difference between "tiny" and "absent", which is exactly the difference MOVE-4e spent a
    /// session establishing. <c>peak</c> is recomputed from the camera's own curve rather than
    /// remembered, so it cannot drift from what the camera did.</para>
    /// </summary>
    private string LandingLine()
    {
        if (_lastLandFallMps < 0f)
            return "landing    (none yet)";
        float peak = SandboxCamera.CameraDipStrengthM
                   * SandboxCamera.DipIntensityFor(_lastLandFallMps);
        return $"landing    fall {_lastLandFallMps,5:F2} m/s   dip peak {peak,7:F4} m"
             + $"   now {_camera.DipNowM,7:F4} m   ramp {SandboxCamera.CameraDipRampPower:F1}";
    }

    /// <summary>The tuning's state on the harness readout, so it is visible with the panel hidden —
    /// and read out of the live session rather than remembered here, same as every other line.</summary>
    private string TuningLine() =>
        _tuning is null ? "tuning     (panel not built)" : _tuning.Session.StatusLine();

    // --- The scripted capture run ---------------------------------------------------------------

    /// <summary>
    /// <c>-Capture</c>: a short scripted drive that photographs the playground and writes the
    /// readout's own numbers beside the pictures.
    ///
    /// <para>It exists so a change to this harness can be checked without a human, and so the jump
    /// measurement can be held against a known figure. <b>MOVE-8 (2026-08-28) re-measured all of
    /// them at Talon's ruled tuning, in engine</b>: this run's held sprint jump measures apex
    /// 1.407 m, distance 6.485 m, airtime 1.067 s, its jog tap 0.300 m, 0.887 m, 0.233 s, and its
    /// NEW double-jump beat 2.402 m, 8.715 m, 1.433 s. (Before the ruling: 1.534 / 7.200 / 0.833
    /// and 0.467 / 1.710 / 0.317, with no double jump to exist, let alone measure.)</para>
    ///
    /// <para><b>Which of those agree with <c>MotorArc</c> and which cannot, stated so the next
    /// reader does not have to work it out.</b> The two APEXES and the whole jog tap match the
    /// derivation to the millimetre — the tap runs on flat spawn ground, and an apex does not care
    /// what it lands on. The two DISTANCES and the two long airtimes do not, and must not: the
    /// scripted sprint launches from the Ridge Run flow course and lands lower than it left, which
    /// is the same course effect MOVE-4e recorded below. The flat numbers <c>MotorArc</c> derives
    /// (held 4.053 m / 0.667 s, double 6.485 m / 1.067 s) are what level constants want; these are
    /// what this course produces. Wildly different numbers out of this
    /// run mean the measurement above is wrong, not the motor.</para>
    ///
    /// <para><b>The 6.192 m / 0.717 s this comment used to quote was stale on its own branch, and
    /// MOVE-4e caught it.</b> MOVE-3b measured those before the Ridge Run flow course existed; the
    /// scripted sprint jump launches from that course and lands on it, and the course's landing
    /// geometry means the body now falls further than it rose. The apex — which does not depend on
    /// the landing plane — is unchanged at 1.534 m, and every jog-tap figure is unchanged.
    /// Measured 3 of 3 runs on this tree and 3 of 3 on its base, identical on both, so it is the
    /// course and not this wave. <b>1.534 m / 0.717 s is still real for a fully-held STANDING jump
    /// on flat ground</b> — it is simply a different jump from this one, which is how one pair of
    /// numbers came to be quoted for both.</para>
    /// </summary>
    private async Task RunCaptureAsync()
    {
        var log = new List<string>();
        ScriptedInput brain = _scripted!;

        await Ticks(40);
        await Shot("01-flow-spawn", log);

        // 02 — a settled sprint. SHORT, and respawned out of afterwards, because a course under
        // construction may be nothing but its placeholder ground: the first run of this capture
        // sprinted 8.64 m/s for two seconds straight off a 30 m placeholder and spent the rest of
        // the take falling. Seventy ticks is past the end of the acceleration ramp (the shot's own
        // speed line proves that, every run) and inside any plausible course.
        brain.Current = Move(1f, sprint: true);
        await Ticks(70);
        await Shot("02-sprint", log);

        // 03 — THE HELD SPRINT JUMP. Jump and JumpHeld both true on the press tick, exactly as a
        // keyboard produces them, and JumpHeld stays true for the whole rise: this is the tall
        // jump, and it is the one MOVE-3b's 1.534 m came off.
        brain.Current = Move(1f, sprint: true) with { Jump = true, JumpHeld = true };
        await Ticks(6);
        await Shot("03-sprint-jump-rising", log);
        await LandThen(log, "sprint jump (held)");
        brain.Current = MoveIntent.None;
        await Ticks(30);
        TeleportTo(_courseIndex);
        await Ticks(30);

        // 03b — THE DOUBLE JUMP, TAKEN AT THE APEX (MOVE-8 scope item 3).
        //
        // New at MOVE-8, because the air jump is new at MOVE-8: AirJumpMode shipped at its exact
        // no-op until Talon ruled the traditional double jump on 2026-08-28, so before this wave
        // there was no second arc to measure.
        //
        // AT THE APEX, and the timing is the measurement rather than a detail. StepAirJump's
        // mode 1 ASSIGNS velocity.Y = JumpVelocity * AirJumpVelocityFraction instead of adding to
        // it, so a press taken while the body is still rising throws away the rise it overwrites.
        // The apex is the first instant at which nothing is discarded, which makes this the
        // ENVELOPE — the reading a "can this ledge be reached" question wants — rather than a
        // typical double jump. It is found by watching Velocity.Y rather than by counting ticks,
        // so it stays right when the arc moves.
        //
        // The press is one tick of a rising Jump EDGE: StepJump consumes edges, so JumpHeld must
        // go false for a tick first or the second press is not an edge at all and nothing fires.
        brain.Current = Move(1f, sprint: true);
        await Ticks(40);
        brain.Current = Move(1f, sprint: true) with { Jump = true, JumpHeld = true };
        await Ticks(2);
        brain.Current = Move(1f, sprint: true) with { JumpHeld = true };   // held, no fresh edge
        for (int i = 0; i < 120 && _avatar.Velocity.Y > 0f; i++)
            await Ticks(1);
        brain.Current = Move(1f, sprint: true);                            // release: arm the edge
        await Ticks(1);
        brain.Current = Move(1f, sprint: true) with { Jump = true, JumpHeld = true };
        await Ticks(2);
        brain.Current = Move(1f, sprint: true) with { JumpHeld = true };
        await Shot("03b-double-jump-rising", log);
        await LandThen(log, "double jump (air jump spent at apex)");
        brain.Current = MoveIntent.None;
        await Ticks(30);
        TeleportTo(_courseIndex);
        await Ticks(30);

        // 04 — THE JOG TAP, from the spawn again. The same press, but JumpHeld is released on the
        // tick after the launch, so the rising-release cut applies: the short jump.
        brain.Current = Move(1f, sprint: false);
        await Ticks(60);
        brain.Current = Move(1f, sprint: false) with { Jump = true, JumpHeld = true };
        await Ticks(1);
        brain.Current = Move(1f, sprint: false); // released — the cut
        await Ticks(5);
        await Shot("04-jog-tap-jump", log);
        await LandThen(log, "jog tap (cut)");
        brain.Current = MoveIntent.None;
        await Ticks(30);

        // 05 — the teleport, onto THE COURSE THIS CAPTURE IS CALIBRATED ON.
        //
        // Named, not counted. This line used to read TeleportTo(_courseIndex + 1) and it is the
        // whole of MOVE-5i: "the other one" meant Calibration for as long as there were two courses
        // and meant Scramble the moment there were three, and because TeleportTo mutates
        // _courseIndex, every beat after this one moved with it — the panel shots and the entire
        // landing-dip A/B. Nothing failed. MOVE-4f's calibrated 17.20 m/s drop simply became a
        // 17.69 m/s one, measured on a pile whose own author says it is jittered rather than
        // measured and is allowed to dead-end.
        int calibration = CourseIndexOf<CalibrationCourse>();
        log.Add($"capture course: {_courses[calibration].CourseName} at roster index {calibration} "
                + $"of {_courses.Count}, selected by identity — every beat from 05 on runs here, and "
                + "a course added to the roster cannot move it (MOVE-5i)");
        TeleportTo(calibration);
        await Ticks(40);
        await Shot("05-after-teleport", log);

        // 06 — the respawn key, from wherever the body wandered to.
        brain.Current = Move(1f, sprint: false);
        await Ticks(45);
        brain.Current = MoveIntent.None;
        await Ticks(20);
        TeleportTo(_courseIndex);
        await Ticks(30);
        await Shot("06-after-respawn", log);

        log.Add($"void-floor catches during this run: {_voidCatches} "
                + "(anything above 0 means a beat ran off its course)");

        await CaptureTuningPanelAsync(log);
        await CaptureLandingDipAsync(log);
        await CaptureScrambleArrivalAsync(log);

        WriteLog(log);

        GD.Print("[move-playground] ---- measured ----");
        foreach (string line in log)
            GD.Print($"[move-playground] {line}");

        if (_quitAfter)
        {
            await Ticks(5);
            GetTree().Quit();
        }
    }

    /// <summary>
    /// <b>The knob panel, photographed and exercised (MOVE-4d).</b> Runs <i>after</i> every arc
    /// above, and only ever moves <c>CameraDipStrengthM</c> — a MOVE-4c stub with no reader
    /// anywhere in the codebase — so no measured figure in this log can be a tuned one.
    ///
    /// <para>File I/O is off for a scripted run, so this exercises the panel and the single writer
    /// without reading or overwriting the tuning a human left in <c>user://movement-tuning.json</c>.
    /// What it proves headed: the panel renders, a knob moves through
    /// <c>MotorTuning.TryApply</c>, reset lands exactly on <c>MotorTuning.Default</c>, and the
    /// mouse is still where the camera left it with the panel open.</para>
    /// </summary>
    private async Task CaptureTuningPanelAsync(List<string> log)
    {
        MotorKnob dip = MotorTuningKnobs.CameraDipStrengthM;

        _tuning.PanelVisible = true;
        await Ticks(4);
        await Shot("07-tuning-panel", log);
        log.Add($"panel: {MotorTuningKnobs.All.Count} rows in "
                + $"{MotorTuningSession.Groups.Count} groups, file I/O "
                + $"{(_tuning.Session.FileIoEnabled ? "ON" : "OFF")}, mouse mode {Input.MouseMode} "
                + "(a scripted run forces Visible in BuildAvatarAndCamera, so this records the "
                + "mode rather than judging it — the panel never sets it either way)");

        bool moved = _tuning.Session.SetKnob(dip, 0.12f);
        await Ticks(2);
        log.Add($"panel: set {dip.Name} -> requested 0.12, live "
                + $"{MotorTuning.Current.CameraDipStrengthM:F2}, writer "
                + $"{(moved ? "accepted" : "REFUSED: " + _tuning.Session.LastRefusal)}, "
                + $"{MotorTuningSession.ChangedCount(MotorTuning.Current)} of "
                + $"{MotorTuningKnobs.All.Count} knobs now differ from the shipped defaults");
        await Shot("08-tuning-panel-moved", log);

        _tuning.Session.ResetAll();
        await Ticks(2);
        log.Add("panel: reset -> "
                + (MotorTuning.Current == MotorTuning.Default
                    ? "exactly MotorTuning.Default"
                    : "NOT MotorTuning.Default — reset is broken"));
        _tuning.PanelVisible = false;
        await Ticks(2);
    }

    /// <summary>
    /// <b>THE LANDING DIP, photographed — MOVE-4f acceptance criteria 1 to 4.</b> Runs after every
    /// arc above and after the panel beat, and it puts the tuning back before it returns, so no
    /// measured figure anywhere else in this log can be a tuned one.
    ///
    /// <para><b>It is an A/B, not an absolute pixel reading, and that is the whole design.</b>
    /// MOVE-4e established the horizon tracker and its 563.17 ± 0.08 px resting baseline, but a
    /// landing moves the horizon for a second reason as well: the follow camera's focus lag settles
    /// downward toward the body for about 700 ms after a touchdown, which MOVE-4e measured at
    /// roughly 18 px all by itself. Comparing one sequence against a remembered number cannot tell
    /// those two apart. So this beat runs <b>the same two landings twice</b> — once at the shipped
    /// <c>CameraDipStrengthM</c> of 0.00 and once at the knob's 0.50 maximum — from the same
    /// teleport, on the same course, with the same scripted input. The focus lag is in both. The
    /// dip is in one. The difference between the two sequences is the dip and nothing else.</para>
    ///
    /// <para><b>Two landings, because choice C's claim has two halves.</b> The drop lands at
    /// <b>17.20 m/s</b>, saturates the ramp, and must visibly move the view; the tap lands at
    /// <b>5.35 m/s</b> at the same tuning and must not. Measured over two runs: the drop moves the
    /// rendered view <b>43.97 px</b> and the tap <b>0.33 px</b>, against an instrument whose
    /// run-to-run floor on a static frame is 0.002 px. The drop is the positive control that makes
    /// the tap's near-absence worth something.</para>
    ///
    /// <para>The envelope is stretched to both knobs' maxima (0.30 s attack, 1.20 s recover) for
    /// MOVE-4e's reason: a 1.5 s window is trivial to photograph, so a negative result at it is far
    /// stronger than a negative result at the 0.31 s shipped envelope.</para>
    /// </summary>
    private async Task CaptureLandingDipAsync(List<string> log)
    {
        // High enough that the fall saturates the ramp: 4 m plus the calibration spawn's own
        // clearance lands at a measured 17.20 m/s against a LandFullFallMps of 14, so the drop is
        // at FULL dip depth and the beat measures the deepest thing the knob can do rather than a
        // fraction of it.
        const float DropHeightM = 4.0f;
        const int BurstShots = 7;
        const int BurstSpacingTicks = 9;      // ~0.15 s at 60 Hz: 7 shots span ~0.9 s of a 1.5 s envelope
        // Longer than the stretched envelope's own 1.50 s, so every beat starts from a camera with
        // no dip left in it. See the drop below for what a shorter wait measured instead.
        const int EnvelopeSettleTicks = 120;  // 2.0 s

        log.Add("dip: the A/B below runs the same drop and the same jog tap at strength 0.00 and "
                + "again at 0.50, envelope at both maxima (attack 0.30 s, recover 1.20 s). The "
                + "camera's focus lag is in both sequences; the dip is in one.");

        foreach (float strength in new[] { 0.00f, 0.50f })
        {
            string tag = strength <= 0f ? "s000" : "s050";
            bool ok = _tuning.Session.SetKnob(MotorTuningKnobs.CameraDipStrengthM, strength)
                    & _tuning.Session.SetKnob(MotorTuningKnobs.CameraDipAttackSec, 0.30f)
                    & _tuning.Session.SetKnob(MotorTuningKnobs.CameraDipRecoverSec, 1.20f);
            await Ticks(2);
            log.Add($"dip {tag}: strength {MotorTuning.Current.CameraDipStrengthM:F2}, attack "
                    + $"{MotorTuning.Current.CameraDipAttackSec:F2} s, recover "
                    + $"{MotorTuning.Current.CameraDipRecoverSec:F2} s, ramp "
                    + $"{MotorTuning.Current.CameraDipRampPower:F1}, writer "
                    + (ok ? "accepted" : "REFUSED: " + _tuning.Session.LastRefusal));

            // --- the drop: a real fall, at full dip depth ---------------------------------------
            //
            // EnvelopeSettleTicks, not a shorter wait, and the first run of this beat is why. The
            // teleport itself is a landing - the calibration spawn sits 1.3 m over its floor, so a
            // respawn lands at 8.78 m/s and arms a real envelope. Photographing a jump before that
            // envelope has unwound photographs the RESPAWN's dip: NotifyLanding's re-trigger rule
            // correctly takes the higher of the new depth and the depth already showing, so a tap
            // arriving mid-envelope inherits a depth twenty times its own. Correct behaviour,
            // useless measurement.
            TeleportTo(_courseIndex);
            await Ticks(EnvelopeSettleTicks);
            _avatar.ServerTeleportTo(SpawnFor(_courseIndex) + Vector3.Up * DropHeightM);
            _avatar.Velocity = Vector3.Zero;
            _airborneFromJump = false;
            await SettleOnGroundAsync();
            await DipBurst($"09-dip-{tag}-drop", BurstShots, BurstSpacingTicks, log);

            // --- the tap: the softest jump this motor makes ---------------------------------------
            //
            // A STANDING tap, not the jogging one the arc beats use, and the difference does not
            // matter to what is being measured: the release cut is vertical, so the apex and
            // therefore the landing speed are the same 0.467 m / 5.27 m/s either way. Standing is
            // what makes it safe to run anywhere — the first version of this beat jogged forward
            // for a second first, ran off the calibration course, and the void floor's catch
            // arrived as a 38 m/s "landing" that saturated the very ramp this beat exists to show
            // a tap cannot reach.
            await Ticks(EnvelopeSettleTicks);                  // the envelope above fully unwinds
            TeleportTo(_courseIndex);
            await Ticks(EnvelopeSettleTicks);                  // and so does the respawn's own
            _scripted!.Current = new MoveIntent { Jump = true, JumpHeld = true };
            await Ticks(1);
            _scripted.Current = MoveIntent.None;               // released — the cut
            await Ticks(4);
            await SettleOnGroundAsync();
            await DipBurst($"09-dip-{tag}-tap", BurstShots, BurstSpacingTicks, log);
            await Ticks(EnvelopeSettleTicks);
        }

        _tuning.Session.ResetAll();
        await Ticks(2);
        log.Add("dip: reset -> "
                + (MotorTuning.Current == MotorTuning.Default
                    ? "exactly MotorTuning.Default (dip back to its shipped 0.00 no-op)"
                    : "NOT MotorTuning.Default — reset is broken"));
    }

    /// <summary>
    /// <b>The scramble's arrival frame and its apron edge — MOVE-5i items 3 and 4, photographed
    /// rather than argued.</b>
    ///
    /// <para><b>It runs dead last, after every measured beat.</b> The scramble is not a course any
    /// number in this log is taken on, and MOVE-5i exists precisely because a beat quietly moved onto
    /// it. Putting this at the end means it cannot move anything: by the time it runs, the arcs, the
    /// panel and the whole dip A/B are already measured, logged and reset.</para>
    ///
    /// <para><b>What the two shots are for.</b> <c>10-scramble-arrival</c> is the frame MOVE-5h found
    /// pointed at bare grey plane, retaken with <see cref="ScrambleCourse.SpawnLookAtLocal"/> in
    /// force — the pile should fill it. <c>11-scramble-apron-edge</c> is the walk MOVE-5h took off
    /// the near edge into a 38 m/s void catch: a full sprint straight at the closest rim, 23 m away,
    /// held long past the distance that used to end in a fall. <b>The assertion is the void-catch
    /// counter</b>, sampled either side of the drive and printed as a delta, because a picture of a
    /// body standing on an apron and a picture of a body respawned onto the same apron look
    /// identical.</para>
    /// </summary>
    private async Task CaptureScrambleArrivalAsync(List<string> log)
    {
        // Sprint speed is 8.64 m/s and the near rim is 23 m from the spawn, so ~160 ticks reaches
        // it. 260 leaves a second and a half of running INTO whatever is there, which is the
        // difference between "the wall stopped me" and "I had not got there yet".
        const int DriveTicks = 260;

        int scramble = CourseIndexOf<ScrambleCourse>();
        TeleportTo(scramble);
        await Ticks(40);
        await Shot("10-scramble-arrival", log);

        Vector3 origin = _courses[scramble].Position;
        Vector3 spawn = SpawnFor(scramble);
        int catchesBefore = _voidCatches;

        // +X: the shortest line from this spawn to an apron edge, and the one MOVE-5h walked. The
        // direction is world-space (Move() is too) rather than camera-relative, so aiming the
        // arrival frame at the pile cannot quietly turn this drive into a walk into the pile.
        _scripted!.Current = new MoveIntent { MoveDir = new Vector3(1f, 0f, 0f), Sprint = true };
        await Ticks(DriveTicks);
        _scripted.Current = MoveIntent.None;
        await Ticks(20);

        float travelled = _avatar.GlobalPosition.X - spawn.X;
        int caught = _voidCatches - catchesBefore;
        log.Add($"scramble apron: sprinted +X for {DriveTicks} ticks from the spawn, travelled "
                + $"{travelled:F2} m to local x {_avatar.GlobalPosition.X - origin.X:F2} "
                + $"(the apron's rim is x 49), ended {(_avatar.Grounded ? "GROUNDED" : "AIRBORNE")} "
                + $"at y {_avatar.GlobalPosition.Y - origin.Y:F2}, void-floor catches during the "
                + $"drive: {caught} "
                + (caught == 0
                    ? "(MOVE-5i item 4: the rim holds)"
                    : "— THE APRON STILL DROPS THE BODY INTO THE VOID"));

        // Back off and turn round before photographing it. The reading above is taken with the body
        // pressed into the parapet, and at that range the follow camera's occlusion clamp has pulled
        // the lens inside the wall — a correct picture of a solid object and a useless one. A few
        // metres back, looking the way the sprint was going, is the frame that shows what stopped
        // it. Camera only: the body's own facing is the motor's business.
        _scripted.Current = new MoveIntent { MoveDir = new Vector3(-1f, 0f, 0f) };
        await Ticks(70);
        _scripted.Current = MoveIntent.None;
        await Ticks(25);
        Vector3 here = _avatar.GlobalPosition;
        _camera.SetOrbit(MovementCourse.YawTowards(here, here + Vector3.Right), _camera.Pitch);
        await Ticks(6);
        await Shot("11-scramble-apron-edge", log);
    }

    /// <summary>Waits for the body to be airborne and then to touch down again, bounded. Returns on
    /// the first grounded tick, which is the tick <c>SandboxAvatar</c> arms the dip on — so a burst
    /// started here begins at the top of the attack ramp rather than somewhere down the release.</summary>
    private async Task SettleOnGroundAsync()
    {
        for (int i = 0; i < 240 && _avatar.Grounded; i++)
            await Ticks(1);
        for (int i = 0; i < 600 && !_avatar.Grounded; i++)
            await Ticks(1);
        // One more tick so this class's OWN _PhysicsProcess has seen the touchdown: the loop above
        // watches the avatar's grounded flag, and the harness's fall-speed accumulator is read on
        // the tick after it flips. Without this the first shot of a burst logs the PREVIOUS
        // landing's fall speed, which reads exactly like a measurement bug.
        await Ticks(1);
    }

    /// <summary>A run of captures across a dip envelope, each one logging what the camera was
    /// actually applying at the moment it was taken — so the pictures and the numbers cannot
    /// disagree about the same frame.</summary>
    private async Task DipBurst(string prefix, int shots, int spacingTicks, List<string> log)
    {
        for (int i = 0; i < shots; i++)
        {
            log.Add($"{prefix}-{i}: fall {_lastLandFallMps:F2} m/s, dip peak "
                    + $"{SandboxCamera.CameraDipStrengthM * SandboxCamera.DipIntensityFor(_lastLandFallMps):F5} m, "
                    + $"dip now {_camera.DipNowM:F5} m, focus Y {_camera.GlobalPosition.Y:F4} m, "
                    + $"lens Y {_camera.CameraNode!.GlobalPosition.Y:F4} m");
            await Shot($"{prefix}-{i}", log);
            await Ticks(spacingTicks);
        }
    }

    /// <summary>Writes the run's numbers beside its pictures. Through <c>System.IO</c> rather than
    /// Godot's <c>FileAccess</c> because <see cref="_outDir"/> is an absolute OS path handed in by
    /// the runner, which is the same reason <see cref="ViewportCapture"/> takes one.</summary>
    private void WriteLog(List<string> log)
    {
        string path = System.IO.Path.Combine(_outDir, "readout.txt");
        try
        {
            System.IO.Directory.CreateDirectory(_outDir);
            System.IO.File.WriteAllLines(path, log);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[move-playground] could not write {path}: {e.Message}");
        }
    }

    /// <summary>
    /// Waits out an airborne body and logs the arc the harness measured for it.
    ///
    /// <para><b>Gated on the arc counter, not on the clock.</b> A beat that runs off the edge of a
    /// course is caught by the void floor and respawned, which grounds the body without ever
    /// completing an arc — and a log line that printed the numbers anyway would be quoting the
    /// <i>previous</i> jump's measurement under this jump's label. That is exactly the class of
    /// quiet wrong number this whole readout exists to make impossible, so it says so instead.</para>
    /// </summary>
    private async Task LandThen(List<string> log, string label)
    {
        int before = _jumpCount;
        for (int i = 0; i < 300 && _jumpCount == before; i++)
            await Ticks(1);

        log.Add(_jumpCount > before
            ? $"{label}: apex {_lastApexM:F3} m, distance {_lastDistanceM:F3} m, "
              + $"airtime {_lastAirtimeSec:F3} s"
            : $"{label}: NO ARC MEASURED — the body never landed (ran off the course, or the "
              + "course has no floor under this beat). The numbers on the readout are an older jump's.");
    }

    private MoveIntent Move(float magnitude, bool sprint) => new()
    {
        MoveDir = new Vector3(0, 0, -1) * magnitude,
        Sprint = sprint,
    };

    private async Task Shot(string name, List<string> log)
    {
        UpdateReadout();
        await Ticks(2);
        string path = System.IO.Path.Combine(_outDir, name + ".png");
        bool ok = await ViewportCapture.SaveAsync(this, path, name);
        float speed = new Vector2(_avatar.Velocity.X, _avatar.Velocity.Z).Length();
        log.Add($"{name}: course {_courses[_courseIndex].CourseName}, speed {speed:F2} m/s, "
                + $"gear {_avatar.Visual.Gear}, {(_avatar.Grounded ? "grounded" : "airborne")}, "
                + $"coyote {_avatar.CoyoteRemainingSec:F3} s, buffer {_avatar.JumpBufferRemainingSec:F3} s"
                + (ok ? "" : "   [CAPTURE FAILED]"));
    }

    private async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
