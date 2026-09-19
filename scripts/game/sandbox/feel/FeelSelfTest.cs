using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// The interaction system's own gate, ported from shader-lab's <c>FeelLab</c> self-test
/// (2026-09-05) and extended with the re-grab case Talon found by playing it.
///
///   Godot_v4.7-stable_mono_win64_console.exe --headless --path . res://tests/scenes/FeelSelfTest.tscn
///
/// Exit 0 is green. It prints one machine-readable line, <c>FEEL-SELFTEST-SUMMARY</c>, so a script
/// greps for a result rather than parsing a report by eye.
///
/// -----------------------------------------------------------------------------------------------
/// IT BUILDS ITS OWN HARNESS, AND DELIBERATELY NOT THE FEEL SANDBOX
/// -----------------------------------------------------------------------------------------------
/// A floor, a bare CharacterBody3D, a Camera3D, an Interactor, and the stage. No SandboxAvatar, no
/// SandboxCamera, no avatar visuals -- what is under test is the interaction system, and loading a
/// rigged glTF and a camera rig to test a spring would make this gate fail for reasons that have
/// nothing to do with the thing it gates. The playable scene (FeelSandboxWorld) is where the two
/// meet, and that meeting is judged by eye, which is the honest split.
///
/// It drives the Interactor's PUBLIC API rather than synthesising key events, for the same reason:
/// what is under test is the system, not Godot's input stack. Targeting is still exercised for
/// real -- the subject is placed, and the test asserts the interactor found the prop by itself.
///
/// It may run HEADLESS. Nothing asserted here touches the GPU: it is physics state, node state and
/// arithmetic.
/// </summary>
public partial class FeelSelfTest : Node3D
{
    private const string HoverShader = "res://resources/shaders/feel/hover_outline.gdshader";

    private CharacterBody3D _subject = null!;
    private Camera3D _camera = null!;
    private Interactor _hand = null!;
    private ShaderMaterial _hoverMat = null!;
    private Node3D _stageRoot = null!;
    private FeelStage.Built _stage = null!;

    private int _testStep;
    private int _testWait;
    private int _testItem;
    private int _pass, _fail;
    private readonly List<string> _failures = new();
    private Interactable? _testSubjectItem;

    /// <summary>
    /// The clear patch of floor the per-item tests run on, and the character's mark in front of it.
    ///
    /// THE ITEM IS BROUGHT TO THE ARENA RATHER THAN THE CHARACTER TO THE ITEM. Standing next to a
    /// prop on the stage puts three or four others inside the reach volume, all scoring within a
    /// few hundredths of each other, so the test asserts "targeting found the crate" and the
    /// interactor -- correctly -- answers "the anvil". That is not a targeting bug; it is a test
    /// that cannot tell a right answer from a wrong one.
    /// </summary>
    private static readonly Vector3 Arena = new(0.0f, 0.60f, 1.20f);
    private static readonly Vector3 ArenaMark = new(0.0f, 0.0f, 2.20f);
    private const float ArenaReach = 2.0f;

    /// <summary>Far corner. Everything not under test goes here, frozen, so the arena holds exactly
    /// one candidate. 5.8 m from the mark against a 2.0 m reach -- a margin, not a coincidence.</summary>
    private static readonly Vector3 Park = new(-2.6f, 0.40f, -3.0f);

    public override void _Ready()
    {
        BuildHarness();
        BuildStage();
        _hand.SetReachRadius(ArenaReach);
        GD.Print("[FeelSelfTest] SELFTEST start");
    }

    private void BuildHarness()
    {
        var floor = new StaticBody3D { Name = "Floor", Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(48, 1, 48) },
        });
        AddChild(floor);

        _subject = new CharacterBody3D { Name = "Subject", Position = ArenaMark };
        _subject.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.7f },
            Position = new Vector3(0, 0.85f, 0),
        });
        AddChild(_subject);

        _camera = new Camera3D { Name = "Camera" };
        AddChild(_camera);
        AimCamera();
    }

    /// <summary>
    /// Stand the camera behind the subject looking down its -Z, which is what the real rig does.
    ///
    /// It is re-aimed every tick rather than parented, because the test TELEPORTS the subject (to
    /// the slot, in the snap cases) and a camera that lagged a teleport would be scoring targeting
    /// against a view axis pointing at where the character used to be.
    /// </summary>
    private void AimCamera()
    {
        Vector3 at = _subject.GlobalPosition;
        _camera.GlobalPosition = at + new Vector3(0, 1.6f, 3.0f);
        _camera.LookAt(at + new Vector3(0, 0.9f, 0), Vector3.Up);
    }

    private void BuildStage()
    {
        _hoverMat = new ShaderMaterial { Shader = GD.Load<Shader>(HoverShader) };
        _stageRoot = new Node3D { Name = "Stage" };
        AddChild(_stageRoot);
        _stage = FeelStage.Build(_stageRoot, _hoverMat);

        if (_hand == null!)
        {
            _hand = new Interactor { Name = "Hand", Carrier = _subject, Camera = _camera };
            _subject.AddChild(_hand);
        }
    }

    private void ResetStage()
    {
        _hand.TryRelease();
        _hand.ForgetAll();
        _stageRoot.QueueFree();
        BuildStage();
        _hand.SetReachRadius(ArenaReach);
    }

    public override void _PhysicsProcess(double delta)
    {
        AimCamera();
        StepSelfTest();
    }

    private void Expect(bool ok, string what)
    {
        if (ok) { _pass++; return; }
        _fail++;
        _failures.Add(what);
        GD.PrintErr($"[FeelSelfTest] SELFTEST FAIL: {what}");
    }

    private void PlaceFor(Interactable it, params Interactable?[] alsoLeaveAlone)
    {
        _subject.Rotation = Vector3.Zero;
        _subject.GlobalPosition = ArenaMark;

        // PARK EVERYTHING ELSE. Each item was released in the arena and simply STAYED there, so the
        // next item was teleported on top of its predecessor and the interactor -- correctly -- kept
        // targeting the one it had already been holding.
        foreach (Interactable other in _stage.Items)
        {
            if (other == it) continue;
            bool exempt = false;
            foreach (Interactable? e in alsoLeaveAlone) if (e == other) { exempt = true; break; }
            if (exempt) continue;
            Freeze(other, Park);
        }

        Freeze(it, Arena);
    }

    /// <summary>
    /// Put a prop somewhere and hold it there.
    ///
    /// KINEMATIC FREEZE, NOT STATIC. A RigidBody3D frozen in STATIC mode does not push a transform
    /// write through to the physics server: the NODE moves, GlobalPosition reports the new value,
    /// and the collider stays exactly where it was -- which reads as broken targeting and is
    /// nothing of the kind.
    /// </summary>
    private static void Freeze(Interactable it, Vector3 at)
    {
        it.Body.LinearVelocity = Vector3.Zero;
        it.Body.AngularVelocity = Vector3.Zero;
        it.Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        it.Body.Freeze = true;
        it.Body.GlobalTransform = new Transform3D(Basis.Identity, at);
    }

    /// <summary>Stand where the HAND ends up over a slot. The slot match is measured from the
    /// carried item, not from the character, so a test that positions the character on the slot
    /// puts the item a hand's length away from it and asserts a miss.</summary>
    private void StandToReach(InteractionSlot slot)
    {
        Vector3 offset = _hand.HandOffset;
        _subject.GlobalPosition = new Vector3(
            slot.GlobalPosition.X - offset.X, 0.0f, slot.GlobalPosition.Z - offset.Z);
    }

    private void StepSelfTest()
    {
        if (_testWait > 0) { _testWait--; return; }

        switch (_testStep)
        {
            // ---- per-item: target, grab, hover, carry, release ---------------------------------
            case 0:
                if (_testItem >= _stage.Items.Count) { _testStep = 99; return; }
                _testSubjectItem = _stage.Items[_testItem];
                PlaceFor(_testSubjectItem);
                // Two ticks: one for the transform to land, one for the reach area's overlap
                // callbacks to fire. Area enter/exit is reported on the physics step AFTER the
                // bodies actually overlap, which is a real ordering property, not a delay to tune.
                _testWait = 4;
                _testStep = 1;
                return;

            case 1:
                Expect(_hand.Target == _testSubjectItem,
                    $"targeting found {_testSubjectItem!.Label} (got {_hand.Target?.Label ?? "null"})");
                Expect(_hand.TryGrab(), $"grab {_testSubjectItem.Label}");
                Expect(_hand.Carried == _testSubjectItem, $"carrying {_testSubjectItem.Label}");
                Expect(_testSubjectItem.Body.Freeze, $"{_testSubjectItem.Label} frozen while held");
                _testWait = 10;
                _testStep = 2;
                return;

            case 2:
            {
                Interactable it = _testSubjectItem!;
                Vector3 p = it.Body.GlobalPosition;
                Expect(p.IsFinite(), $"{it.Label} carry transform is finite");
                Expect(_hand.CarryLag < 2.0f, $"{it.Label} lag stayed bounded ({_hand.CarryLag:0.00} m)");
                // The cue stays lit on the thing in your hand. Asserted because it silently stopped
                // doing so the moment held items were taken off their collision layer.
                Expect(it.HoverAmount > 0.9f && it.HullVisible,
                    $"{it.Label} keeps its cue while carried (hover {it.HoverAmount:0.00}, "
                    + $"drawn {it.HullVisible})");

                bool snap = it.Release == Interactable.ReleaseMode.Snap;
                Expect(_hand.TryRelease(), $"release {it.Label}");
                Expect(!it.Held, $"{it.Label} no longer held");
                Expect(it.Body.CollisionLayer != 0,
                    $"{it.Label} collision layer restored on release (got {it.Body.CollisionLayer})");
                if (snap)
                    Expect(it.Slot == null && !it.Body.Freeze,
                        $"{it.Label} snapped with no slot in range -> tumbled ({_hand.LastVerdict})");
                else
                    Expect(!it.Body.Freeze, $"{it.Label} tumble handed back to physics");

                _testItem++;
                _testWait = 3;
                _testStep = 0;
                return;
            }

            // ---- snap into a real slot ---------------------------------------------------------
            //
            // REBUILD THE STAGE FIRST. The per-item loop has carried, dropped and parked every prop,
            // and one of them is the orb that starts life seated in a slot. Testing occupancy
            // against a stage the previous test has already emptied asserts nothing.
            case 99:
                ResetStage();
                _testWait = 6;
                _testStep = 100;
                return;

            case 100:
            {
                InteractionSlot? slot = FindSlot("Slot_Vessel");
                Interactable? jug = FindItem("jug");
                if (slot == null || jug == null)
                {
                    Expect(false, "stage has a vessel slot and a jug");
                    _testStep = 199;
                    return;
                }
                PlaceFor(jug, FindItem("orb A"));
                _testWait = 4;
                _testStep = 101;
                return;
            }

            case 101:
            {
                InteractionSlot slot = FindSlot("Slot_Vessel")!;
                Interactable jug = FindItem("jug")!;
                _hand.TryGrab();
                Expect(_hand.Carried == jug, "grabbed the jug for the snap test");
                StandToReach(slot);
                // LONG ENOUGH FOR THE CARRY TO SETTLE, and that is not padding. Teleporting the
                // character gives the hand anchor an enormous one-tick velocity, the spring chases
                // it, and a release taken before that decays inherits it.
                _testWait = 40;
                _testStep = 102;
                return;
            }

            case 102:
            {
                InteractionSlot slot = FindSlot("Slot_Vessel")!;
                Interactable jug = FindItem("jug")!;
                _hand.TryRelease();
                Expect(jug.Slot == slot, $"jug claimed the vessel slot ({_hand.LastVerdict})");
                Expect(slot.Occupant == jug, "slot reports the jug as its occupant");
                Expect(jug.Settling, "settle animation is running rather than teleporting");
                _testWait = Mathf.CeilToInt(_hand.SettleTime * 70.0f) + 6;
                _testStep = 103;
                return;
            }

            case 103:
            {
                InteractionSlot slot = FindSlot("Slot_Vessel")!;
                Interactable jug = FindItem("jug")!;
                Expect(!jug.Settling, "settle finished");
                float d = jug.Body.GlobalPosition.DistanceTo(slot.RestPose(jug.Body.GlobalTransform).Origin);
                Expect(d < 0.02f, $"jug landed on the slot rest pose (off by {d * 100.0f:0.0} cm)");
                // KINEMATIC, NOT STATIC, and this assertion is the guard on the fix for the bug
                // in case 104 below. Both modes freeze the body out of the solver; only Static
                // also makes it invisible to an Area3D, which is what stopped a slotted item from
                // ever being a candidate again. See the settle note in Interactable._Process.
                Expect(jug.Body.Freeze, "a slotted item is frozen out of the solver");
                Expect(jug.Body.FreezeMode == RigidBody3D.FreezeModeEnum.Kinematic,
                    $"a slotted item stays on the kinematic freeze (got {jug.Body.FreezeMode})");
                // THE COLLIDER, NOT THE NODE. A rigid body's node transform and its physics-server
                // transform can disagree, and it fails silently in the direction that matters: the
                // prop LOOKS placed and is not actually there.
                var served = (Transform3D)PhysicsServer3D.BodyGetState(
                    jug.Body.GetRid(), PhysicsServer3D.BodyState.Transform);
                Expect(served.Origin.DistanceTo(jug.Body.GlobalPosition) < 0.01f,
                    "collider followed the settle (node vs server, off by "
                    + $"{served.Origin.DistanceTo(jug.Body.GlobalPosition) * 100.0f:0.0} cm)");
                // PARK ORB A BEFORE ASKING A TARGETING QUESTION, and the reason is worth keeping.
                //
                // Orb A sits seated in the neighbouring slot, 1.27 m from where the character is
                // standing and well inside a 2.0 m reach, so it is a legitimate second candidate --
                // and it out-scored the jug the first time this ran. That is correct behaviour from
                // the interactor and a broken assumption in the test.
                //
                // It only became visible because of the fix. A seated item used to be Static-frozen
                // and therefore invisible to the reach area, so the arena LOOKED isolated while
                // actually relying on the bug to hide the neighbour. Restoring the isolation
                // explicitly is what makes the assertion below mean what it says.
                Interactable? seated = FindItem("orb A");
                if (seated != null) Freeze(seated, Park);
                _testWait = 8;
                _testStep = 104;
                return;
            }

            // ---- TAKE IT BACK OFF THE SHELF -----------------------------------------------------
            //
            // TALON FOUND THIS BY PLAYING IT, 2026-09-05: "There's no way to remove the items you
            // have previously placed on the snapping platform." Everything above passed and none of
            // it covered this, because every case put an item DOWN and the next case teleported a
            // different one in. Nothing ever reached for something already in a slot.
            //
            // It is the exact case the interactor's own candidate bookkeeping is weakest at.
            // Grabbing sets the held body's collision layer to 0, which fires body_exited and drops
            // it from _near; releasing restores the layer. For a TUMBLED item that is harmless --
            // it is handed back to physics with velocity, it moves, the broadphase re-pairs it and
            // body_entered fires again. A SNAPPED item never moves again: it settles, goes Static,
            // and sits there. Whether the pair is ever re-tested is then an engine-internals
            // question, and this asserts the answer instead of reasoning about it.
            case 104:
                Expect(_hand.NearCount > 0,
                    $"a slotted item is still a candidate (in reach {_hand.NearCount})");
                Expect(_hand.Target == FindItem("jug"),
                    $"targeting finds the item sitting in the slot (got {_hand.Target?.Label ?? "null"})");
                _testStep = 105;
                return;

            case 105:
            {
                InteractionSlot slot = FindSlot("Slot_Vessel")!;
                Interactable jug = FindItem("jug")!;
                Expect(_hand.TryGrab(), "the item can be taken back out of the slot");
                Expect(_hand.Carried == jug, "carrying the item that was in the slot");
                // Leaving a slot frees it immediately, not when the carry ends -- a slot still
                // claimed by something being carried away is a slot nothing else can use.
                Expect(jug.Slot == null, "the item no longer belongs to a slot");
                Expect(slot.IsFree, "vacating the slot freed it for the next item");
                _hand.TryRelease();
                _testStep = 199;
                return;
            }

            // ---- occupied slot ------------------------------------------------------------------
            case 199:
                ResetStage();
                _testWait = 6;
                _testStep = 200;
                return;

            case 200:
            {
                InteractionSlot? slot = FindSlot("Slot_Orb");
                Interactable? orbB = FindItem("orb B");
                if (slot == null || orbB == null)
                {
                    Expect(false, "stage has an orb slot and a second orb");
                    _testStep = 299;
                    return;
                }
                Expect(!slot.IsFree, "the pre-occupied orb slot starts occupied");
                // Orb A is left where it is: it IS the occupancy under test.
                PlaceFor(orbB, FindItem("orb A"));
                _testWait = 4;
                _testStep = 201;
                return;
            }

            case 201:
            {
                InteractionSlot slot = FindSlot("Slot_Orb")!;
                Interactable orbB = FindItem("orb B")!;
                _hand.TryGrab();
                Expect(_hand.Carried == orbB, "grabbed the second orb");
                StandToReach(slot);
                _testWait = 40;
                _testStep = 202;
                return;
            }

            case 202:
            {
                Interactable orbB = FindItem("orb B")!;
                _hand.TryRelease();
                Expect(orbB.Slot == null, "the second orb did not take an occupied slot");
                Expect(_hand.LastVerdict.Contains("REJECTED"), $"rejection was reported ({_hand.LastVerdict})");
                Expect(!orbB.Body.Freeze, "the rejected orb was handed back to physics");
                _testStep = 299;
                return;
            }

            // ---- spring stability at the extremes -----------------------------------------------
            //
            // The naive spring integration blows up as omega*dt approaches 1, and the tuning range
            // exposed in the panel REACHES that. It does not fail loudly: the item flies off and the
            // transform goes NaN a frame later. This runs the carry at both ends of the dial.
            case 299:
                ResetStage();
                _testWait = 6;
                _testStep = 300;
                return;

            case 300:
            {
                Interactable it = _stage.Items[0];
                PlaceFor(it);
                _hand.LightResponse = 40.0f;
                _hand.HeavyResponse = 40.0f;
                _testWait = 4;
                _testStep = 301;
                return;
            }

            case 301:
                _hand.TryGrab();
                Expect(_hand.Carried != null, "grabbed for the stability run");
                _testWait = 120;
                _testStep = 302;
                return;

            case 302:
            {
                Interactable? it = _hand.Carried;
                if (it == null) { Expect(false, "still carrying after the stability run"); _testStep = 400; return; }
                Vector3 p = it.Body.GlobalPosition;
                Expect(p.IsFinite(), $"spring stayed finite at omega=40 (pos {p})");
                Expect(p.DistanceTo(_subject.GlobalPosition) < 5.0f,
                    $"spring stayed near the hand at omega=40 ({p.DistanceTo(_subject.GlobalPosition):0.0} m)");
                _hand.TryRelease();
                _testStep = 400;
                return;
            }

            // ---- the throw key --------------------------------------------------------------------
            //
            // New here: the lab had no throw, so nothing ever asserted that heft governs it. The
            // claim is that a 22 kg anvil leaves the hand slower than a 0.4 kg block; if that ever
            // stops being true, the heaviest item in the game throws like the lightest and the one
            // dial this system is built around is not reaching its loudest action.
            case 400:
                ResetStage();
                _testWait = 6;
                _testStep = 401;
                return;

            case 401:
            {
                Interactable? block = FindItem("block");
                if (block == null) { Expect(false, "stage has a light block"); _testStep = 500; return; }
                PlaceFor(block);
                _testWait = 4;
                _testStep = 402;
                return;
            }

            case 402:
            {
                Interactable block = FindItem("block")!;
                _hand.TryGrab();
                Expect(_hand.Carried == block, "grabbed the block to throw it");
                _testWait = 20;
                _testStep = 403;
                return;
            }

            case 403:
            {
                Interactable block = FindItem("block")!;
                Expect(_hand.TryThrow(), "threw the block");
                Expect(_hand.LastVerdict.StartsWith("THROW"), $"throw reported itself ({_hand.LastVerdict})");
                Expect(!block.Body.Freeze, "a thrown item is handed back to physics");
                _lightThrowSpeed = block.Body.LinearVelocity.Length();
                Expect(_lightThrowSpeed > 4.0f,
                    $"the light item actually left the hand ({_lightThrowSpeed:0.00} m/s)");

                Interactable? anvil = FindItem("anvil");
                if (anvil == null) { Expect(false, "stage has an anvil"); _testStep = 500; return; }
                PlaceFor(anvil);
                _testWait = 4;
                _testStep = 404;
                return;
            }

            case 404:
                _hand.TryGrab();
                Expect(_hand.Carried == FindItem("anvil"), "grabbed the anvil to throw it");
                _testWait = 20;
                _testStep = 405;
                return;

            case 405:
            {
                Interactable anvil = FindItem("anvil")!;
                Expect(_hand.TryThrow(), "threw the anvil");
                float heavy = anvil.Body.LinearVelocity.Length();
                Expect(heavy < _lightThrowSpeed,
                    $"heft governs the throw: anvil {heavy:0.00} m/s < block {_lightThrowSpeed:0.00} m/s");
                _testStep = 500;
                return;
            }

            default:
                Finish();
                return;
        }
    }

    private float _lightThrowSpeed;

    private InteractionSlot? FindSlot(string name)
    {
        foreach (InteractionSlot s in _stage.Slots) if (s.Name == name) return s;
        return null;
    }

    private Interactable? FindItem(string contains)
    {
        foreach (Interactable i in _stage.Items) if (i.Label.Contains(contains)) return i;
        return null;
    }

    private void Finish()
    {
        int total = _pass + _fail;
        foreach (string f in _failures) GD.Print($"[FeelSelfTest] FAILED: {f}");
        // One machine-readable line, named so a script can grep for it -- a human-readable report
        // that a CI job has to parse by eye is a report nothing checks.
        GD.Print($"FEEL-SELFTEST-SUMMARY total={total} pass={_pass} fail={_fail} "
            + $"exit={(_fail == 0 ? 0 : 1)}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }
}
