using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Ui;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Headless self-test for the sandbox mechanics layer. Two tiers:
///   1. Pure logic — CarryController, StumbleLogic, VoiceRangePublisher run against
///      plain fakes, no scene tree at all.
///   2. Physics integration — a minimal world (ground + avatar + props) ticked through
///      real physics frames, driving the avatar through the same public entry points
///      the local player and (later) the net layer use.
/// No server, matchmaking, or network peer anywhere. Prints one line per check and a
/// final "SANDBOX-TEST OVERALL: PASS|FAIL"; exit code 0 only if everything passed.
/// Run: Godot --headless --path . res://scenes/game/sandbox/SandboxSelfTest.tscn
/// </summary>
public partial class SandboxSelfTest : Node3D
{
    private sealed class FakeCarryable : ICarryable
    {
        public bool IsHeld { get; private set; }
        public float MassKg { get; init; } = 4f;
        public Vector3? LastImpulse { get; private set; }
        public void OnPickedUp(CarryController holder) => IsHeld = true;
        public void OnDropped() => IsHeld = false;
        public void OnThrown(Vector3 impulse) { IsHeld = false; LastImpulse = impulse; }
    }

    /// <summary>A carryable that amplifies the holder's published voice range — the generic
    /// contract any future range-changing prop implements (see VoiceRange.Megaphone, the
    /// foundation's one preset for an amplified range).</summary>
    private sealed class FakeAmplifiedSource : ICarryable, IVoiceRangeSource
    {
        public bool IsHeld { get; private set; }
        public float MassKg => 2f;
        public VoiceRange Range => VoiceRange.Megaphone;
        public void OnPickedUp(CarryController holder) => IsHeld = true;
        public void OnDropped() => IsHeld = false;
        public void OnThrown(Vector3 impulse) => IsHeld = false;
    }

    /// <summary>Test-scripted intent: the "network peer" of the test world.</summary>
    private sealed class ScriptedIntentSource : IIntentSource
    {
        public MoveIntent Current;
        public MoveIntent NextIntent(double delta)
        {
            MoveIntent intent = Current;
            // Edge-flags auto-clear so a single set acts like a key press.
            Current = Current with { Jump = false, Interact = false, Throw = false };
            return intent;
        }
    }

    private readonly List<(string Name, bool Ok)> _results = new();
    private bool _finished;

    public override void _Ready()
    {
        // Watchdog: a hung physics await must still produce a verdict. Sized well above the
        // suite's real runtime (~25 s of real-time physics frames) and well under
        // Run-SandboxTest.ps1's own 90 s process timeout, so a genuine hang is still caught.
        GetTree().CreateTimer(60.0).Timeout += () =>
        {
            if (_finished)
                return;
            Check("watchdog_no_hang", false);
            Finish();
        };

        RunPureTests();
        _ = RunPhysicsTestsAsync();
    }

    // --- Tier 1: pure logic --------------------------------------------------------

    private void RunPureTests()
    {
        // Carry rules.
        var carrier = new CarryController();
        var crate = new FakeCarryable();
        var ball = new FakeCarryable { MassKg = 1.5f };
        int pickedEvents = 0, droppedEvents = 0;
        carrier.PickedUp += _ => pickedEvents++;
        carrier.Dropped += _ => droppedEvents++;

        Check("carry_pickup", carrier.TryPickUp(crate) && carrier.Held == crate && crate.IsHeld && pickedEvents == 1);
        Check("carry_single_slot", !carrier.TryPickUp(ball) && carrier.Held == crate);

        var thief = new CarryController();
        Check("carry_contested", !thief.TryPickUp(crate) && thief.Held == null);

        Check("carry_encumbrance", carrier.SpeedFactor < 1f && carrier.SpeedFactor >= 0.55f);
        Check("carry_drop", carrier.Drop() && carrier.Held == null && !crate.IsHeld && droppedEvents == 1);
        Check("carry_drop_empty", !carrier.Drop());
        Check("carry_unencumbered", carrier.SpeedFactor == 1f);

        // MECHANICS-BIBLE 6: the encumbrance formula must define its behaviour at the
        // extremes, not just mid-range. MassKg is scene-authorable (Carryable forwards
        // Godot's Mass), so a designer typo reaches this formula directly. Untreated,
        // 1/(1 + mass*0.055) has a pole at mass == -18.18: the denominator crosses zero
        // and Mathf.Max then SELECTS the huge positive result — a prop that makes you
        // faster the heavier it is authored to be.
        Check("carry_speed_factor_zero_mass", CarryController.ComputeSpeedFactor(0f) == 1f);
        Check("carry_speed_factor_negative_mass_clamps",
            CarryController.ComputeSpeedFactor(-5f) == 1f);
        Check("carry_speed_factor_at_pole_is_finite",
            float.IsFinite(CarryController.ComputeSpeedFactor(-1f / 0.055f)));
        Check("carry_speed_factor_past_pole_never_speeds_up",
            CarryController.ComputeSpeedFactor(-100f) <= 1f);
        Check("carry_speed_factor_huge_mass_floors",
            CarryController.ComputeSpeedFactor(1e9f) == 0.55f);
        Check("carry_speed_factor_nan_mass_is_finite",
            float.IsFinite(CarryController.ComputeSpeedFactor(float.NaN)));

        var thrower = new CarryController();
        thrower.TryPickUp(ball);
        Check("carry_throw", thrower.Throw(new Vector3(0, 0, -7)) && thrower.Held == null
            && ball.LastImpulse == new Vector3(0, 0, -7));
        Check("carry_throw_empty", !thrower.Throw(Vector3.Up));

        // Voice range publishing (a held IVoiceRangeSource swaps the broadcaster's range).
        var publisher = new VoiceRangePublisher();
        var amplified = new FakeAmplifiedSource();
        VoiceRange? observed = null;
        publisher.VoiceRangeChanged += r => observed = r;

        Check("voice_default_normal", publisher.Current == VoiceRange.Normal);
        publisher.UpdateFromHeld(amplified);
        Check("voice_amplifies", publisher.Current == VoiceRange.Megaphone && observed == VoiceRange.Megaphone);
        publisher.UpdateFromHeld(new FakeCarryable());
        Check("voice_plain_item_normal", publisher.Current == VoiceRange.Normal);
        publisher.UpdateFromHeld(amplified);
        publisher.UpdateFromHeld(null);
        Check("voice_drop_reverts", publisher.Current == VoiceRange.Normal);
        Check("voice_matches_speaker_tuning", VoiceRange.Normal.UnitSize == 6f && VoiceRange.Normal.MaxDistanceMeters == 24f);
    }

    // --- Tier 2: physics integration -------------------------------------------------

    private async System.Threading.Tasks.Task RunPhysicsTestsAsync()
    {
        // Minimal world: a slab, one avatar with a scripted brain, a crate.
        var ground = new StaticBody3D { Name = "Ground", Position = new Vector3(0, -0.5f, 0) };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(50, 1, 50) } });
        AddChild(ground);

        var brain = new ScriptedIntentSource();
        // Pinned to a specific roster entry rather than left on the default. Every check below
        // that reads the visual rig — carry bob, carry pose, mesh tilt clamp — was written
        // against the harvested part contract (arms, feet, sprout). The classic greybox is the
        // code-built body that carries every part of that contract, so naming it keeps those
        // checks measuring what they were built to measure with no asset to load.
        var avatar = new SandboxAvatar
        {
            Name = "TestAvatar", AvatarKey = AvatarVisual.BoxKidAvatarKey, Position = new Vector3(0, 1, 0),
        };
        AddChild(avatar);
        avatar.IntentSource = brain;

        // Props start well off the avatar's test runway (it walks -Z from the origin).
        var crate = new Carryable { Kind = Carryable.Shape.Crate, Mass = 4, Position = new Vector3(6, 0.5f, 6) };
        AddChild(crate);

        await Ticks(60);
        Check("phys_avatar_grounds", avatar.IsOnFloor());

        // Locomotion: hold forward for a second.
        brain.Current = new MoveIntent { MoveDir = new Vector3(0, 0, -1) };
        Vector3 start = avatar.GlobalPosition;
        await Ticks(60);
        brain.Current = MoveIntent.None;
        Check("phys_avatar_moves", start.DistanceTo(avatar.GlobalPosition) > 2f);
        await Ticks(30);

        // Held-item waddle: the carry-anchor bob (AvatarVisual.CarryBobOffset) must be ~still when
        // standing and ride the gait while walking — a gentle sway, never a wild swing. Guards the
        // waddle-synced carry polish (a held prop follows this offset through its anchor-chase).
        float restBob = 0f;
        for (int i = 0; i < 24; i++)
        {
            await Ticks(1);
            restBob = Mathf.Max(restBob, Mathf.Abs(avatar.Visual.CarryBobOffset.Y));
        }
        brain.Current = new MoveIntent { MoveDir = new Vector3(0, 0, -1) };
        await Ticks(30); // reach walking speed / waddle saturation
        float walkBobPeak = 0f, walkMagPeak = 0f;
        for (int i = 0; i < 40; i++)
        {
            await Ticks(1);
            Vector3 bob = avatar.Visual.CarryBobOffset;
            walkBobPeak = Mathf.Max(walkBobPeak, bob.Y);
            walkMagPeak = Mathf.Max(walkMagPeak, Mathf.Abs(bob.X) + Mathf.Abs(bob.Y));
        }
        brain.Current = MoveIntent.None;
        await Ticks(30);
        Check("carry_bob_still_at_rest", restBob < 0.02f);
        Check("carry_bob_rides_waddle", walkBobPeak > 0.015f && walkMagPeak < 0.08f);

        // Jump: leaves the floor, then returns.
        brain.Current = brain.Current with { Jump = true };
        await Ticks(10);
        bool airborne = !avatar.IsOnFloor();
        await Ticks(90);
        Check("phys_avatar_jumps_and_lands", airborne && avatar.IsOnFloor());

        // Pickup: walk the crate into reach conceptually — just teleport it near.
        crate.GlobalPosition = avatar.GlobalPosition + new Vector3(0, 0.4f, -1f);
        await Ticks(5);
        Check("phys_pickup_succeeds", avatar.InteractPickDrop() && avatar.Carry.Held == (ICarryable)crate);
        await Ticks(30);
        // Carry pose: arms ease forward while holding. Driven per-frame from the "am I holding"
        // signal (here the offline CarryController), so it engages without a pickup event.
        Check("carry_pose_engages", avatar.Visual.CarryWeight > 0.5f);
        Check("phys_carried_item_tracks_anchor",
            crate.IsHeld && crate.Freeze && crate.GlobalPosition.DistanceTo(avatar.GlobalPosition) < 1.6f);
        Check("phys_carried_item_encumbers", avatar.Carry.SpeedFactor < 1f);

        // --- Gap 0 (ANIM-1): is the held item actually in the HAND, at full sprint? ------------
        //
        // THE POSITIVE CONTROL IS THE POINT OF THIS BLOCK. "The prop follows the anchor" was
        // already green for a year while the anchor itself ignored every rotation the body made —
        // because every existing measurement compared the prop against the anchor, or against the
        // avatar's ROOT transform, and both of those agreed with each other while both were wrong.
        // So this measures against a third thing: where the posed BODY actually put the hand
        // (AvatarVisual.PoseGlobalTransform * the anchor's rest offset), and it computes BOTH
        // formulas on the same frame at the same pose:
        //
        //   legacy   = avatarRoot * (visual.Position + rest + carryBob)   <- what shipped for a year
        //   anchored = CarryAnchorGlobalTransform.Origin                  <- what ships now
        //
        // Same frame rather than two builds, deliberately: two runs cannot guarantee the same body
        // pose, and the whole quantity being measured is a function of the pose. If `legacy` does not
        // show a large error here, the method cannot prove the fix and this test is worthless — hence
        // the explicit control assertion below rather than only the one that passes.
        // MEASURED ACROSS THE ACCELERATION RAMP, NOT AT A SETTLED SPRINT (re-stated MOVE-1,
        // 2026-08-16). This block used to run 75 ticks to let the run lean saturate and then sample
        // — which worked because the shipped lean was a CONSTANT held at every sprint. That lean is
        // gone: it now encodes acceleration, so a settled sprint is upright and the sample window
        // has to be the ramp. Same measurement, moved to where the pose it needs actually happens.
        brain.Current = new MoveIntent { MoveDir = new Vector3(0, 0, -1), Sprint = true };
        float legacyWorst = 0f, anchoredWorst = 0f, launchTilt = 0f;
        for (int i = 0; i < 55; i++) // ~0.92 s: the whole 0.96 s ramp to a dead sprint
        {
            await Ticks(1);
            Vector3 rest = avatar.Proportions.CarryAnchorRestLocal;
            Vector3 hand = avatar.Visual.PoseGlobalTransform * rest;
            Vector3 legacy = avatar.GlobalTransform
                * (avatar.Visual.Position + rest + avatar.Visual.CarryBobOffset);
            Vector3 anchored = avatar.CarryAnchorGlobalTransform.Origin;
            legacyWorst = Mathf.Max(legacyWorst, legacy.DistanceTo(hand));
            anchoredWorst = Mathf.Max(anchoredWorst, anchored.DistanceTo(hand));
            launchTilt = Mathf.Min(launchTilt, avatar.Visual.BodyTiltX); // most negative = furthest forward
        }
        // Hold the sprint until the ramp is over and nothing is changing, then read the pose again.
        await Ticks(70);
        float steadyTilt = avatar.Visual.BodyTiltX;
        float steadySpeed = new Vector2(avatar.Velocity.X, avatar.Velocity.Z).Length();
        Gear steadyGear = avatar.Visual.Gear;
        float cadence = avatar.Visual.CadenceHz;
        float reach = avatar.Visual.StanceReachM;
        // Then let go and read it a third time, mid-brake.
        brain.Current = MoveIntent.None;
        float brakeTilt = 0f;
        for (int i = 0; i < 25; i++)
        {
            await Ticks(1);
            brakeTilt = Mathf.Max(brakeTilt, avatar.Visual.BodyTiltX); // most positive = furthest back
            // The Gap 0 measurement continues through the brake, which is the harder of the two
            // gestures and therefore the deepest pose the body reaches in ordinary play.
            Vector3 rest = avatar.Proportions.CarryAnchorRestLocal;
            Vector3 hand = avatar.Visual.PoseGlobalTransform * rest;
            legacyWorst = Mathf.Max(legacyWorst, (avatar.GlobalTransform
                * (avatar.Visual.Position + rest + avatar.Visual.CarryBobOffset)).DistanceTo(hand));
            anchoredWorst = Mathf.Max(anchoredWorst,
                avatar.CarryAnchorGlobalTransform.Origin.DistanceTo(hand));
        }
        await Ticks(20);

        // RAW NUMBERS IN THE LOG, not just a verdict: this is the measurement the packet owes, and a
        // pass/fail line cannot be compared across runs or against a base commit.
        GD.Print($"[sandbox] hand-to-prop across the launch ramp: legacy {legacyWorst:F4} m, " +
                 $"anchored {anchoredWorst:F4} m " +
                 $"(rest offset |{avatar.Proportions.CarryAnchorRestLocal.Length():F3}| m)");
        GD.Print($"[sandbox] lean encodes acceleration: launch {launchTilt:F4} rad, " +
                 $"steady {steadyTilt:F4} rad at {steadySpeed:F2} m/s ({steadyGear}), " +
                 $"braking {brakeTilt:F4} rad; gait {cadence:F2} steps/s, reach {reach:F3} m, " +
                 $"leg {avatar.Visual.LegLengthM:F3} m");

        // THE LEAN IS THE DELIVERABLE HERE, and these three lines are what "encodes acceleration"
        // means as assertions rather than as prose: forward while getting up to speed, UPRIGHT once
        // there (this is the one that would have failed against every previous build), back while
        // braking.
        Check("accel_leans_the_body_forward", launchTilt < -0.06f);
        Check("steady_speed_stands_the_body_up", Mathf.Abs(steadyTilt) < 0.05f);
        Check("braking_leans_the_body_back", brakeTilt > 0.06f);
        // …and it never gets anywhere near the ~0.52 rad the shipped run lean held constantly.
        //
        // 0.30 -> 0.36 (second amendment). The peak now includes the anticipation flinch, and at the
        // END of a ramp the two ADD rather than oppose: the acceleration drops to zero as the speed
        // saturates, which is itself an onset, so the body pitches a little further forward and then
        // settles. That is the overshoot-and-settle the amendment asks for and it measured 0.251
        // launching / 0.287 braking — comfortably under this, and still a third under the 0.520 the
        // old constant lean held at every speed. Widened deliberately rather than left one hundredth
        // of a radian from flaking.
        Check("lean_stays_far_below_the_old_run_pitch",
            Mathf.Abs(launchTilt) < 0.36f && Mathf.Abs(brakeTilt) < 0.36f);
        // The gait is a real gait: a human cadence and a stride the leg can actually reach.
        Check("gait_cadence_is_human", cadence > 2.0f && cadence < 4.0f);
        Check("gait_reach_fits_the_leg", reach > 0.05f && reach <= avatar.Visual.LegLengthM);
        Check("sprint_reaches_the_sprint_gear", steadyGear == Gear.Sprint);

        // The Gap 0 control, unchanged in intent: the legacy formula must be visibly WRONG at a
        // posed body, or the assertion under it proves nothing.
        Check("gap0_control_legacy_anchor_leaves_the_hand", legacyWorst > 0.10f);
        Check("gap0_anchor_rides_the_hand", anchoredWorst < 0.05f);

        // --- FOOT SLIP: the number that decides whether "planted" is true --------------------
        //
        // LocomotionTests proves the arithmetic without an engine. This proves the ENGINE agrees:
        // it walks the avatar at a steady speed and, every frame a foot is in stance, measures how
        // far that foot's ground contact point moved in WORLD space. A skating gait shows up here
        // as millimetres per frame; the shipped waddle had no fore-aft foot motion at all, so its
        // slip was the full ground speed — 0.144 m per frame at a dead sprint.
        //
        // Steady speed on purpose: during an acceleration ramp the derived reach is legitimately
        // changing under the foot, and a few millimetres of that is the price of deriving the gait
        // from a speed that is itself moving. What must be zero is the steady case.
        await RunFootSlipTestAsync();

        // --- ANIM-M3: the authored clip library, in the engine, headless ------------------------
        //
        // Deliberately here rather than in a capture script. Whether a track RESOLVES and whether it
        // actually MOVES anything are two different questions, and the second one is what
        // the original harvested creature failed silently for as long as it shipped - five clips, every one of
        // them dead, because the harvest deletes the nodes they target (ANIM-M0 S1.4). A screenshot
        // cannot tell those apart; a measured joint delta can, and it runs on any machine.
        await RunAuthoredClipTestsAsync();

        // Drop: a light toss arc, then physics resumes and the crate settles.
        Check("phys_drop_succeeds", avatar.InteractPickDrop() && avatar.Carry.Held == null);
        Check("phys_drop_toss_arc", crate.LinearVelocity.Y > 0.5f);
        await Ticks(90);
        Check("phys_dropped_item_lands", !crate.IsHeld && !crate.Freeze && crate.GlobalPosition.Y < 0.7f);
        Check("carry_pose_relaxes", avatar.Visual.CarryWeight < 0.1f);

        // Fall/landing floor invariants: through a big drop and its landing, the
        // physics collider must stay upright and grounded and the visual mesh's
        // tilt/lift must stay inside the floor-safe clamps. (Regression guard for
        // the old tumble reaction that rolled the mesh through the floor.)
        avatar.GlobalPosition = new Vector3(0, 6f, 0);
        avatar.Velocity = Vector3.Zero;
        bool upright = true, meshClamped = true;
        // RIG-1's additions, measured over the same fall and landing. The crouch is the landing
        // absorb's real knee flex (it replaced LandCompressY's mesh squash), so it is a NEW way for a
        // body to move vertically and it gets its own invariants rather than riding on the pose's.
        bool crouchSane = true, feetOnFloor = true, limbsFinite = true;
        float deepestCrouch = 0f, deepestKnee = 0f;
        for (int i = 0; i < 100; i++)
        {
            await Ticks(1);
            upright &= avatar.Rotation.X == 0f && avatar.Rotation.Z == 0f
                && avatar.GlobalPosition.Y > -0.01f;
            meshClamped &= Mathf.Abs(avatar.Visual.BodyTiltX) <= AvatarVisual.MaxBodyTilt + 0.02f
                && avatar.Visual.BodyOffsetY >= -0.001f;

            // The knees may SINK the rig, never lift it, and never by more than a sixth of the leg —
            // the same class of claim BodyOffsetY >= 0 makes about the pose, in the other direction.
            float drop = avatar.Visual.CrouchDropM;
            crouchSane &= drop >= -0.0001f && drop <= avatar.Visual.LegLengthM * 0.16f;
            deepestCrouch = Mathf.Max(deepestCrouch, drop);
            deepestKnee = Mathf.Max(deepestKnee, avatar.Visual.KneeBendRad(left: true));

            // THE POINT OF THE CROUCH: the hips drop because the knees bent, so the FEET must not.
            // A crouch that lowered the rig without the legs shortening to match would put both
            // ankles through the floor, which is precisely the failure a hip-drop invites.
            feetOnFloor &=
                avatar.Visual.FootContactGlobal(left: true).Y >= avatar.GlobalPosition.Y - 0.02f
                && avatar.Visual.FootContactGlobal(left: false).Y >= avatar.GlobalPosition.Y - 0.02f;

            // The NaN prohibition, positively controlled on a live transform rather than only in
            // LimbIk's unit tests: a NaN reaching a Transform3D is how a body silently vanishes, and
            // it would show here as an unrenderable rig with no log line anywhere.
            limbsFinite &= FiniteRig(avatar.Visual);
        }
        Check("phys_fall_collider_upright", upright);
        Check("phys_fall_mesh_clamped", meshClamped);
        Check("phys_fall_lands", avatar.IsOnFloor());
        Check("rig_crouch_bounded_and_never_lifts", crouchSane);
        Check("rig_feet_stay_on_the_floor_through_the_absorb", feetOnFloor);
        Check("rig_no_nan_in_any_limb_transform", limbsFinite);
        // The absorb must actually HAPPEN on a fall this big, or the three checks above are green on a
        // body that never moved — the shape of a test that passes because the feature is missing.
        Check("rig_landing_absorb_is_expressed",
            !avatar.Visual.HasKnees || (deepestCrouch > 0.005f && deepestKnee > 0.20f));

        // Control is NEVER lost: immediately after that hard landing the avatar can
        // still act — grab a crate on the very next tick, then drop it again.
        crate.GlobalPosition = avatar.GlobalPosition + new Vector3(0, 0.4f, -1f);
        crate.LinearVelocity = Vector3.Zero;
        await Ticks(2);
        Check("phys_control_never_locked", avatar.InteractPickDrop()
            && avatar.Carry.Held == (ICarryable)crate && avatar.InteractPickDrop());

        // --- Launch guard: grabbing an item you are STANDING ON must not fling you.
        // The old failure mode: the held prop's collider ejecting the holder as the
        // prop detaches / moves to the hand. Rest the avatar on top of a crate, grab it
        // through the intent source (so the pickup runs inside _PhysicsProcess exactly
        // like the Interact key), and assert the avatar only ever begins to fall — never
        // gains height or upward velocity — and that the held collider is off at once.
        var standCrate = new Carryable { Kind = Carryable.Shape.Crate, Mass = 4, Position = new Vector3(12, 0.22f, 0) };
        AddChild(standCrate);
        await Ticks(30); // let the crate settle flat on the ground
        avatar.GlobalPosition = standCrate.GlobalPosition + new Vector3(0, 0.9f, 0);
        avatar.Velocity = Vector3.Zero;
        await Ticks(70); // avatar drops and comes to rest on top of the crate
        bool restingOnCrate = avatar.IsOnFloor() && avatar.GlobalPosition.Y > 0.3f;
        float yBeforeGrab = avatar.GlobalPosition.Y;
        brain.Current = brain.Current with { Interact = true };
        bool grabbedUnderfoot = false;
        float snapDist = -1f;
        float peakY = yBeforeGrab, peakYVel = float.MinValue;
        for (int i = 0; i < 20; i++)
        {
            await Ticks(1);
            if (!grabbedUnderfoot && avatar.Carry.Held == (ICarryable)standCrate)
            {
                grabbedUnderfoot = true;
                // Snap invariant (bug 2): the very first tick it is held the prop must
                // already sit at the carry anchor — no multi-frame slide up from rest.
                //
                // Measured against the anchor this prop RIDES, not the bare mount (W7-8): an
                // armful-posed slot item sits a little above the mount so the hands end up under
                // it rather than inside it, and comparing against the mount would red this check
                // while the invariant it names — "already there on tick one" — still holds
                // exactly. The instrument follows the contract; the tolerance is untouched.
                snapDist = standCrate.GlobalPosition.DistanceTo(
                    standCrate.EffectiveCarryAnchor(avatar.Carry.AnchorProvider!()).Origin);
            }
            peakY = Mathf.Max(peakY, avatar.GlobalPosition.Y);
            peakYVel = Mathf.Max(peakYVel, avatar.Velocity.Y);
        }
        Check("phys_grab_underfoot_no_launch",
            restingOnCrate && grabbedUnderfoot && peakY <= yBeforeGrab + 0.12f && peakYVel <= 0.5f);
        Check("phys_held_item_collision_disabled",
            standCrate.CollisionLayer == 0 && standCrate.CollisionMask == 0);
        Check("phys_pickup_snaps_to_anchor", grabbedUnderfoot && snapDist >= 0f && snapDist < 0.15f);

        // Dropping must not shove the holder either — the prop re-enables collision at
        // the hand and tosses clear; the avatar's own vertical velocity stays gravity-only.
        float dropPeakYVel = float.MinValue;
        avatar.InteractPickDrop(); // drop
        for (int i = 0; i < 15; i++)
        {
            await Ticks(1);
            dropPeakYVel = Mathf.Max(dropPeakYVel, avatar.Velocity.Y);
        }
        Check("phys_drop_no_holder_shove", avatar.Carry.Held == null && dropPeakYVel <= 0.5f);

        // Edge cases named in the brief — none may launch the holder.
        // (a) Grabbing an item that rests on ANOTHER object (stacked crates): standing
        //     on the top crate and grabbing it drops the holder onto the lower crate.
        var lowerCrate = new Carryable { Kind = Carryable.Shape.Crate, Mass = 6, Position = new Vector3(-12, 0.22f, 0) };
        var upperCrate = new Carryable { Kind = Carryable.Shape.Crate, Mass = 4, Position = new Vector3(-12, 0.66f, 0) };
        AddChild(lowerCrate);
        AddChild(upperCrate);
        await Ticks(40); // let the stack settle
        avatar.GlobalPosition = new Vector3(-12, upperCrate.GlobalPosition.Y + 0.9f, 0);
        avatar.Velocity = Vector3.Zero;
        await Ticks(60);
        float stackedPeakVel = float.MinValue;
        bool stackedGrab = avatar.InteractPickDrop(); // grabs the nearest = top crate
        for (int i = 0; i < 20; i++)
        {
            await Ticks(1);
            stackedPeakVel = Mathf.Max(stackedPeakVel, avatar.Velocity.Y);
        }
        Check("phys_grab_stacked_no_launch", stackedGrab && avatar.Carry.Held != null && stackedPeakVel <= 0.5f);
        avatar.InteractPickDrop();
        await Ticks(10);

        // (b) Grabbing while AIRBORNE (falling toward the item) must not launch either.
        var airCrate = new Carryable { Kind = Carryable.Shape.Crate, Mass = 4, Position = new Vector3(-8, 0.22f, 8) };
        AddChild(airCrate);
        await Ticks(30);
        avatar.GlobalPosition = airCrate.GlobalPosition + new Vector3(0, 1.2f, 0);
        avatar.Velocity = Vector3.Zero;
        await Ticks(6); // a few frames of fall — still airborne, item within reach
        bool airGrab = avatar.InteractPickDrop();
        float airPeakVel = float.MinValue;
        for (int i = 0; i < 20; i++)
        {
            await Ticks(1);
            airPeakVel = Mathf.Max(airPeakVel, avatar.Velocity.Y);
        }
        Check("phys_grab_airborne_no_launch", airGrab && airPeakVel <= 0.5f);
        avatar.InteractPickDrop();
        await Ticks(10);

        // Freed-while-held hardening: QueueFree is deferred, so a prop something just
        // claimed (a drain swallowing a coin) still looks valid for the rest of the
        // frame. Grabbing it must be refused, and a prop freed WHILE held must empty
        // the hands within a tick instead of leaving Carry.Held dangling at a disposed
        // node (SpeedFactor dereferences it every tick — this was a live crash loop).
        var dyingProp = new Carryable { Kind = Carryable.Shape.Ball, Position = avatar.GlobalPosition + new Vector3(0, 0.4f, -1f) };
        AddChild(dyingProp);
        await Ticks(3);
        dyingProp.QueueFree();
        Check("carry_refuses_dying_prop", !avatar.Carry.TryPickUp(dyingProp) && avatar.Carry.Held == null);
        await Ticks(2);
        var doomedProp = new Carryable { Kind = Carryable.Shape.Ball, Position = avatar.GlobalPosition + new Vector3(0, 0.4f, -1f) };
        AddChild(doomedProp);
        await Ticks(3);
        bool doomedGrabbed = avatar.Carry.TryPickUp(doomedProp);
        doomedProp.QueueFree();
        await Ticks(2);
        Check("carry_releases_freed_prop",
            doomedGrabbed && avatar.Carry.Held == null && avatar.Carry.SpeedFactor == 1f);

        // Camera floor invariant: even pitched to the look-up clamp, the SpringArm
        // must keep the camera above the ground plane.
        var cam = new SandboxCamera { Name = "TestCamera" };
        AddChild(cam);
        // MOVE-4f: attaching IS the registration, and this is the in-engine proof of it. Before
        // MOVE-4f, SandboxAvatar._followCamera was assigned only inside ConfigureNetworkedInstance's
        // owned-client branch, so every dev harness in the repo drove a camera the avatar had never
        // heard of and the landing dip was a permanent no-op there (MOVE-4e, AC3). The check reads
        // BEFORE and AFTER on the same body: an "after" on its own would pass on an avatar that had
        // arrived already registered, which is exactly the mistake that hid the defect.
        bool followCameraBeforeAttach = avatar.HasFollowCamera;
        cam.Attach(avatar);
        Check("camera_attach_registers_the_follow_camera",
            !followCameraBeforeAttach && avatar.HasFollowCamera);
        // Read off the constant, not a literal (CATCH-1): the whole point of raising PitchMax was
        // that the floor guarantee no longer depends on the clamp being small, and a test pinned
        // to 0.30 would have gone on passing at an angle nobody can now reach.
        cam.SetOrbit(0f, SandboxCamera.PitchMax);
        await Ticks(40);
        Check("phys_camera_stays_above_floor", cam.CameraNode!.GlobalPosition.Y > 0.05f);

        // Camera degenerate-cast invariant: pushing into a wall with the orbit facing the
        // same wall puts the spring-arm's cast origin (focus + look-ahead) ON the wall
        // plane — the probe run showed the arm then flickers full↔zero every tick, putting
        // the camera alternately outside the room (through the wall: the "blocks of
        // texture" symptom) and at the pivot. The focus must stay clamped in free space:
        // the camera never crosses the wall, and never teleports between ticks.
        var camWall = new StaticBody3D { Name = "CamWall", Position = new Vector3(20, 2, -6) };
        var camWallShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, 4, 0.8f) } };
        camWall.AddChild(camWallShape);
        AddChild(camWall);
        avatar.GlobalPosition = new Vector3(20, 0.5f, -2);
        avatar.Velocity = Vector3.Zero;
        cam.SetOrbit(Mathf.Pi, -0.10f); // arm points INTO the wall the avatar pushes against
        await Ticks(30); // let avatar land and camera settle before sampling
        brain.Current = new MoveIntent { MoveDir = new Vector3(0, 0, -1), Sprint = true };
        const float wallFaceZ = -5.6f; // wall front face; camera must never pass it
        float maxCamStep = 0f;
        bool camStayedInside = true;
        Vector3 prevCamPos = cam.CameraNode!.GlobalPosition;
        for (int i = 0; i < 240; i++)
        {
            await Ticks(1);
            Vector3 p = cam.CameraNode!.GlobalPosition;
            maxCamStep = Mathf.Max(maxCamStep, p.DistanceTo(prevCamPos));
            prevCamPos = p;
            camStayedInside &= p.Z > wallFaceZ - 0.05f;
        }
        brain.Current = MoveIntent.None;
        Check("camera_never_crosses_wall", camStayedInside);
        Check("camera_never_teleports_at_wall", maxCamStep < 0.8f);
        cam.SetOrbit(0f, 0.30f);
        await Ticks(30);

        // Teleport cut invariant: a commanded teleport (TV portal, reset-to-spawn) must
        // CUT the camera to the destination, not lerp the lagged focus across the map —
        // the probe run showed the chase drags the spring-arm's cast origin through every
        // wall in between (a second-long screenful of wall faces). Two ticks after a 40m
        // teleport the focus must already be at the avatar, not 26% of the way there.
        avatar.GlobalPosition = new Vector3(-20, 0.5f, 15);
        avatar.Velocity = Vector3.Zero;
        await Ticks(2);
        // cam.FocusHeight, not a literal 0.7: the follow camera frames a fraction of the
        // character's own measured crown now (AvatarProportions.CameraFocusHeightM), so a test
        // written against the original body's number would quietly start asserting the wrong point
        // the first time this block ran on a different character.
        Check("camera_cuts_on_teleport",
            cam.GlobalPosition.DistanceTo(avatar.GlobalPosition + Vector3.Up * cam.FocusHeight) < 1.5f);
        await Ticks(20);

        // Reconciliation render-tracking invariant: the netcode hides server corrections by
        // offsetting the RENDERED mesh (_visualError -> Visual.Position) while the body sim
        // stays honest. Everything else that follows the avatar on screen — the camera's
        // focus point and the carry anchor (held items) — must follow that same corrected
        // point, or the camera and held props visibly detach from the character whenever a
        // correction fires. Inject a fixed offset through the same seam Reconcile uses
        // (offline roles never drain it) and assert both trackers land on the corrected spot.
        Vector3 renderErr = new(0.8f, 0f, 0.5f);
        Vector3 anchorBeforeErr = avatar.CarryAnchorGlobalTransform.Origin;
        avatar.TestInjectVisualError(renderErr);
        await Ticks(40); // camera focus lerp (stiffness 9) fully converges well inside this
        Check("camera_focus_tracks_render_position",
            cam.GlobalPosition.DistanceTo(avatar.RenderGlobalPosition + Vector3.Up * cam.FocusHeight) < 0.05f);
        Check("carry_anchor_tracks_render_position",
            (avatar.CarryAnchorGlobalTransform.Origin - anchorBeforeErr - renderErr).Length() < 0.05f);
        avatar.TestInjectVisualError(Vector3.Zero);
        await Ticks(40); // camera settles back before anything below reuses it

        // MECHANICS-BIBLE 2: pause is a state change, and EVERY world-anchored widget must
        // follow it — not just the one whose author remembered. The interact chip was
        // suppressed and the nameplate was not, so names drew over the pause menu. Both now
        // read WorldUi.Suppressed.
        //
        // This lives down here, after the camera exists: UpdateNameplate projects through
        // the live camera, so with no camera in the tree the plate is hidden for an
        // unrelated reason and "hides when suppressed" would pass trivially.
        avatar.DisplayName = "TestPuffling";
        await Ticks(2);
        bool nameShownUnsuppressed = avatar.NameplateVisible;
        WorldUi.Suppressed = true;
        await Ticks(2);
        bool nameHiddenSuppressed = !avatar.NameplateVisible;
        WorldUi.Suppressed = false;
        await Ticks(2);
        Check("ui_nameplate_shows_unsuppressed", nameShownUnsuppressed);
        Check("ui_nameplate_hides_when_world_ui_suppressed", nameHiddenSuppressed);
        Check("ui_nameplate_returns_after_suppression", avatar.NameplateVisible);

        // INTERACTION-BIBLE 4: client and server reach must be one number with a stated
        // tolerance. They were 1.5 m and 3.0 m — the server accepting grabs at double what
        // the client would ever attempt, decided by nobody.
        Check("interact_server_reach_covers_client_reach",
            PropManager.TestGrabRange >= SandboxAvatar.PickupRadius);
        Check("interact_server_reach_is_bounded",
            PropManager.TestGrabRange <= SandboxAvatar.PickupRadius * 1.75f);

        // INTERACTION-BIBLE 1: affordance. This poll used to live inline in the OFFLINE
        // sandbox only, so networked play had no shimmer and no key chip — props read as
        // scenery. Exercising the shared helper directly covers both worlds at once.
        var glowCrate = new Carryable
        {
            Kind = Carryable.Shape.Crate,
            Position = avatar.GlobalPosition + new Vector3(0, 0.3f, -0.8f),
        };
        AddChild(glowCrate);
        await Ticks(5);

        SandboxAvatar? viewer = avatar;
        var highlighter = new InteractHighlighter(() => viewer);
        highlighter.Tick(1.0); // one interval is enough; the poll is throttled, not per-frame
        Check("interact_highlight_marks_nearest", highlighter.Highlighted == glowCrate && glowCrate.Highlighted);

        // Outline shell (tide-polish-pass BUILD-SPEC §4): must appear on the exact prop
        // InteractHighlighter picked — the same InteractTargeting.Pick winner the "E" chip
        // tracks, never re-derived.
        await Ticks(1);
        Check("interact_outline_shows_on_targeted_prop", glowCrate.OutlineVisible);

        // Held-object rule (§8.5): the outline must never render on a held item, even in the
        // exact race the 10 Hz poll interval opens — Highlighted is still stale-true here
        // because nobody has ticked the highlighter since the grab. If the outline relied on
        // Highlighted alone it would flash on the item now riding in the player's hand; the
        // IsHeld gate in Carryable._PhysicsProcess closes that window by construction.
        var raceHolder = new CarryController();
        bool grabbedWhileHighlighted = raceHolder.TryPickUp(glowCrate);
        await Ticks(1);
        Check("interact_outline_never_shows_on_held_item",
            grabbedWhileHighlighted && glowCrate.IsHeld && glowCrate.Highlighted && !glowCrate.OutlineVisible);
        raceHolder.Drop();
        await Ticks(1);

        // Viewer gone (headless peer, or the avatar freed on a reconnect) must not leave a
        // shimmer burning on a prop nobody is looking at.
        viewer = null;
        highlighter.Tick(1.0);
        Check("interact_highlight_clears_without_viewer",
            highlighter.Highlighted == null && !glowCrate.Highlighted);
        await Ticks(1);
        Check("interact_outline_hides_when_not_targeted", !glowCrate.OutlineVisible);
        glowCrate.QueueFree();
        await Ticks(2);

        // --- Highlight glow must BREATHE AROUND authored emission, never erase it --------
        // Crate.tscn/Sphere.tscn author emission_energy_multiplier so the gold props clear
        // the Environment's glow gate on their own. The candidate-highlight pulse in
        // Carryable._PhysicsProcess writes that same material_override every frame, so its
        // resting target must BE the authored energy — not absolute zero, which silently
        // deleted the authored bloom a few frames after _Ready.
        var gold = GD.Load<PackedScene>("res://scenes/game/props/Crate.tscn").Instantiate<Carryable>();
        gold.Name = "GoldCrate";
        gold.Position = new Vector3(20, 0.3f, 20);
        var goldMesh = gold.GetNode<MeshInstance3D>("Visual/MeshInstance3D");
        // Read BEFORE the node enters the tree: this is the value the .tscn authored, not a
        // constant, so retuning the scene retunes the test instead of breaking it.
        float authoredGlow = ((StandardMaterial3D)goldMesh.MaterialOverride).EmissionEnergyMultiplier;
        AddChild(gold);
        var goldMat = (StandardMaterial3D)goldMesh.MaterialOverride;

        await Ticks(40); // pre-fix decay is 0.8333^40 = 7.0e-4 of the authored value
        // W7-3 (2026-08-30): the fixture guard was `authoredGlow > 1f` and it is now `> 0f`.
        // THE CLAIM THIS CHECK MAKES IS THE SECOND CLAUSE — "the resting emission is still whatever
        // the scene authored" — and that clause is correctly DERIVED, which is why it passed
        // unchanged when Crate.tscn went 2.5 -> 0.55 for Talon's notes 7 and 16. The first clause
        // was a hardcoded magnitude smuggled in beside the derived one: it asserted that the
        // authored value happens to exceed 1, which is not what the check is named for and not
        // what it is for. It also contradicted the comment four lines up — "not a constant, so
        // retuning the scene retunes the test instead of breaking it" — which is the promise the
        // literal broke. What the guard is genuinely FOR is that the fixture emits at all: at
        // authoredGlow == 0 the comparison `x >= 0 * 0.99` passes trivially and proves nothing.
        // `> 0f` is that guard exactly; `> 1f` was that guard plus a remembered number.
        Check("glow_authored_emission_survives_rest",
            authoredGlow > 0f && goldMat.EmissionEnergyMultiplier >= authoredGlow * 0.99f);

        // ...and the highlight must still read as a highlight ON TOP of that baseline: a
        // ±10%-of-2.5 wobble is not an affordance. Sample one full breath (2*pi/6 s = 63
        // frames at 60 Hz) after the lerp has settled.
        gold.Highlighted = true;
        await Ticks(25);
        float goldPeak = 0f, goldTrough = float.MaxValue;
        for (int i = 0; i < 66; i++)
        {
            await Ticks(1);
            float e = goldMat.EmissionEnergyMultiplier;
            goldPeak = Mathf.Max(goldPeak, e);
            goldTrough = Mathf.Min(goldTrough, e);
        }
        Check("glow_highlight_reads_above_authored_rest",
            goldPeak >= authoredGlow * 1.5f && goldTrough >= authoredGlow);

        gold.Highlighted = false;
        await Ticks(40);
        Check("glow_returns_to_authored_after_highlight",
            Mathf.Abs(goldMat.EmissionEnergyMultiplier - authoredGlow) <= authoredGlow * 0.01f);

        // Regression guard for the code-built fallback (offline Sandbox path, baseline 0):
        // a purely proportional pulse would multiply that zero and remove the affordance
        // entirely. The legacy 0.15..0.55 amplitude must survive unchanged there.
        var plain = new Carryable { Kind = Carryable.Shape.Crate, Position = new Vector3(-20, 0.3f, 20) };
        AddChild(plain);
        var plainMat = (StandardMaterial3D)plain.GetNode<Node3D>("Visual")
            .GetChild<MeshInstance3D>(0).MaterialOverride;
        await Ticks(15);
        float plainRest = plainMat.EmissionEnergyMultiplier;
        plain.Highlighted = true;
        await Ticks(25);
        float plainPeak = 0f;
        for (int i = 0; i < 66; i++)
        {
            await Ticks(1);
            plainPeak = Mathf.Max(plainPeak, plainMat.EmissionEnergyMultiplier);
        }
        Check("glow_codebuilt_fallback_still_highlights_from_zero",
            plainRest <= 0.02f && plainPeak >= 0.45f && plainPeak <= 0.6f);

        await RunProportionsTestsAsync();
        await RunAuthoredBodyTestsAsync();
        await RunAchievementGateTestsAsync();

        Finish();
    }

    /// <summary>
    /// <b>A SCRIPTED BODY EARNS NOTHING</b> (W7-8, 2026-08-30, from the master review).
    ///
    /// <para><b>The defect.</b> <see cref="SandboxAvatar.ConfigureAsNetworked"/> built an
    /// <c>AchievementRuntime</c> for every OWNER-role avatar, and a suite bot owns its own avatar.
    /// <c>Run-AimTest</c>'s scripted 2 s → 6 s aim hold clears <c>AchievementTracker.ToughGuyHoldSec</c>
    /// (3.0 s), and <c>AchievementStore</c> writes into <c>user://settings.cfg</c> — a path that
    /// resolves by project NAME, so every worktree on this machine shares ONE real profile. It was
    /// already measured on Talon's live file: <c>tough_guy=true</c> was the only achievement key
    /// present, precisely the one a bot can reach.</para>
    ///
    /// <para><b>Why the proof is here and not on disk.</b> The obvious demonstration — run the
    /// harness that used to earn it and show the key absent afterwards — requires a profile that
    /// does not already have the key, and the only profile on this machine is the player's real
    /// one. Deleting a player's earned achievement to prove a test is worse than the bug it
    /// proves, so this measures the same fact one layer earlier and touches no file: given the
    /// exact intent source a bot is driven by, the runtime that would do the writing is never
    /// built.</para>
    ///
    /// <para><b>Both directions, in one place.</b> The absence check ("a bot builds none") is worth
    /// nothing without the positive control immediately beside it ("a person's avatar builds one"),
    /// because a gate wired to <c>false</c> would satisfy the first alone. The human arm uses the
    /// real <c>LocalInputIntentSource</c>, not a stand-in — and
    /// <c>BotAchievementGateTests.OnlyLocalInputClaimsToBeHumanInput</c> pins that it is the only
    /// class in the assembly that may claim to be one.</para>
    /// </summary>
    private async System.Threading.Tasks.Task RunAchievementGateTestsAsync()
    {
        // THE BOT ARM. ScriptedAimIntentSource is the literal source Run-AimTest drives its raiser
        // with, wrapped around the same deterministic walk brain — not an approximation of one.
        var botBody = new SandboxAvatar { Name = "9001", Position = new Vector3(120, 1, 120) };
        AddChild(botBody);
        await Ticks(2);
        var scripted = new ScriptedAimIntentSource(
            new DeterministicWalkIntentSource(botBody, 6.0), raiseAtSec: 2.0, lowerAtSec: 6.0);
        botBody.ConfigureAsNetworked(isOwner: true, source: scripted);
        await Ticks(2);
        Check("achievements_scripted_bot_tracks_nothing", !botBody.TracksAchievements);

        // THE POSITIVE CONTROL. Same method, same owner role, same tick — the ONLY difference is
        // that a person is driving. The camera is constructed but never attached: this source only
        // holds the reference, and attaching would want a viewport this headless suite has not got.
        var camera = new SandboxCamera { Name = "GateCamera", HandlesPauseToggle = false };
        AddChild(camera);
        var human = new LocalInputIntentSource(camera);
        var playerBody = new SandboxAvatar { Name = "9002", Position = new Vector3(126, 1, 126) };
        AddChild(playerBody);
        await Ticks(2);
        playerBody.ConfigureAsNetworked(isOwner: true, source: human);
        await Ticks(2);
        Check("achievements_real_player_still_tracks", playerBody.TracksAchievements);

        // A REMOTE PROXY never tracks, whoever is driving the body it mirrors — unchanged by W7-8
        // and pinned here so the new gate cannot be read as the only thing holding it up.
        var proxyBody = new SandboxAvatar { Name = "9003", Position = new Vector3(132, 1, 132) };
        AddChild(proxyBody);
        await Ticks(2);
        proxyBody.ConfigureAsNetworked(isOwner: false, source: null);
        await Ticks(2);
        Check("achievements_remote_proxy_tracks_nothing", !proxyBody.TracksAchievements);

        // CELEBRATE-1 (2026-09-04): the all-bubbles celebration asks the SAME question through the
        // SAME seam, so it is measured here rather than in a parallel self-test that could drift
        // away from this one. Three arms, same three bodies, and the middle one is the positive
        // control: without it, a gate wired to false would satisfy the two absence checks alone.
        Check("celebrate_scripted_bot_is_not_a_person", !botBody.IsHumanDriven);
        Check("celebrate_real_player_is_a_person", playerBody.IsHumanDriven);
        Check("celebrate_remote_proxy_is_not_a_person", !proxyBody.IsHumanDriven);
        // And the pure decision the gate actually makes, both directions. ForceForTest is the
        // suite's own override (--celebrate-force) and must be the ONLY thing that can open the
        // gate for a body nobody is driving.
        Check("celebrate_gate_refuses_without_a_person",
            !Sail.Game.Bubble.BubbleCelebration.ShouldCelebrate(humanPresent: false, forced: false));
        Check("celebrate_gate_opens_for_a_person",
            Sail.Game.Bubble.BubbleCelebration.ShouldCelebrate(humanPresent: true, forced: false));

        botBody.QueueFree();
        playerBody.QueueFree();
        proxyBody.QueueFree();
        camera.QueueFree();
        await Ticks(2);
    }

    // --- The player character is a FILE, and the fallback catches a file that fails ----------

    /// <summary>
    /// <b>INTEG-1's four claims, in the engine, on the body a player actually gets.</b>
    ///
    /// <para>1. <c>PreferredAvatarKey</c> resolves to the authored <c>.glb</c> — measured off a
    /// built instance's <see cref="AvatarVisual.ResolvedModelPath"/>, never off the roster table,
    /// because the roster states an intention and the instance states an outcome.</para>
    ///
    /// <para>2. The eight declared-absent parts produce NO error, and — the positive control, which
    /// is the half that matters — with the declaration emptied all eight show up as undeclared
    /// misses. That is what separates "the declaration silences exactly what is really gone" from
    /// "the declaration silences everything", and the two look identical from a clean log.</para>
    ///
    /// <para>3. The authored asset drives the new rig: knees, elbows and a waist all detected, and
    /// the thigh/shin split measured off the file's own joint origins rather than declared.</para>
    ///
    /// <para>4. The fallback works, forced rather than argued (Talon's ruling; an untested fallback
    /// is not a fallback). The load is made to fail, the primitive takes over, and it is checked for
    /// the thing that would make a silent fallback worthless — that it has real geometry standing at
    /// the right height, not an empty rig.</para>
    /// </summary>
    private async System.Threading.Tasks.Task RunAuthoredBodyTestsAsync()
    {
        // BODY-1 (2026-08-28): the AUTHORED key, which is no longer the PLAYED key.
        //
        // This whole block is INTEG-1's authored-asset contract — the file really loads, the
        // absent parts are declared, the head value break is a per-row declaration, and the
        // fallback works when the load is forced to fail. Every one of those is a claim about a
        // Build.AuthoredRig row, and Talon's 2026-08-28 ruling made the played body a code-built
        // one (greybox_classic), which has no file to load, nothing to declare absent and no load
        // that can fail. Pointed at PreferredAvatarKey it did not go stale quietly — it went red
        // in five places, which is the good failure, and the fix is to aim it at the body it was
        // always describing rather than to weaken it.
        //
        // boxkid is that body: the only surviving Build.AuthoredRig player row, authored by
        // assets/creatures/boxkid/build_boxkid.py precisely so a file-backed player body would
        // still exist. The played body gets its own, different check below — being code-built is
        // a property to assert, not a hole to leave.
        //
        // BODY-2 (2026-08-28) — Talon: "The BoxKid.glb is the one I want." — MERGED the two cases:
        // boxkid is now the played body as well as the authored one, so this block and the played-
        // body block below name the same key. Both are KEPT and neither is collapsed into the
        // other, because they assert different things about it: this one is the authored-ASSET
        // contract (the file loads, the absences are declared, a forced failure falls back), and
        // the one below is the DEFAULT-BODY contract (the picker's row and the world's fallback
        // build the thing a player actually is). Those came apart once already, which is the entire
        // reason BODY-1 existed, and merging them here would delete the check that caught it.
        string key = AvatarVisual.BoxKidAvatarKey;
        var carrier = new Node3D { Name = "AuthoredBodyRig", Position = new Vector3(240, 40, 240) };
        AddChild(carrier);
        var visual = new AvatarVisual { Name = "AuthoredBody" };
        carrier.AddChild(visual);

        // 1. THE GAME LOADS THE FILE.
        AvatarVisual.ResetUndeclaredMissingParts();
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);
        GD.Print($"[sandbox] authored body: key '{key}' -> '{visual.ResolvedModelPath}' " +
                 $"(from file: {visual.ResolvedFromFile})");
        // Renamed by BODY-1: it is the AUTHORED body that comes from a file. Saying
        // "player_character" here would now be false, and a test whose name lies is worse than a
        // test that is missing.
        Check("authored_body_is_loaded_from_a_file",
            visual.ResolvedFromFile
            && visual.ResolvedModelPath.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)
            && ResourceLoader.Exists(visual.ResolvedModelPath));

        // 2a. The declaration covers everything the file actually lacks.
        System.Collections.Generic.IReadOnlySet<string> declared = AvatarVisual.DeclaredAbsentPartsFor(key);
        GD.Print($"[sandbox] authored body: {AvatarVisual.UndeclaredMissingParts.Count} undeclared " +
                 $"missing part(s) [{string.Join(", ", AvatarVisual.UndeclaredMissingParts)}]; " +
                 $"{declared.Count} declared absent [{string.Join(", ", declared)}]");
        Check("authored_body_has_no_undeclared_missing_parts",
            AvatarVisual.UndeclaredMissingParts.Count == 0 && declared.Count > 0);

        // 3. The rig the file drives, measured off the file.
        GD.Print($"[sandbox] authored body: crown {visual.BodyBounds.End.Y:F4} m, " +
                 $"eye {(visual.EyeCentre?.Y ?? -1f):F4} m, knees {visual.HasKnees}, " +
                 $"elbows {visual.HasElbows}, waist {visual.HasWaist}, " +
                 $"thigh {visual.ThighLengthM:F4} + shin {visual.ShinLengthM:F4} = leg {visual.LegLengthM:F4} m");
        Check("authored_body_drives_the_knee_elbow_waist_rig",
            visual.HasKnees && visual.HasElbows && visual.HasWaist
            && visual.ThighLengthM > 0.05f && visual.ShinLengthM > 0.05f
            && Mathf.Abs(visual.ThighLengthM + visual.ShinLengthM - visual.LegLengthM) < 1e-4f);

        // The two dimensions that go stale SILENTLY if the authored asset drifts: two xUnit files
        // read GreyboxAvatarBody.CrownM / EyeY, and nothing else compares them against the body
        // that is actually played. Now something does.
        Check("authored_body_matches_the_constants_the_rest_of_the_game_derives_from",
            Mathf.Abs(visual.BodyBounds.End.Y - GreyboxAvatarBody.CrownM) <= 0.005f
            && visual.EyeCentre != null
            && Mathf.Abs(visual.EyeCentre.Value.Y - GreyboxAvatarBody.EyeY) <= 0.005f);

        // 3b. THE HEAD VALUE BREAK, and it is a per-row DECLARATION rather than a global rule
        //     (AVATAR-5). The greybox's Head is the top slice of one revolved profile, so painting
        //     it a value lighter than the Torso renders as a hard horizontal band across a smooth
        //     dome — the defect Talon could see. Its row declares HeadIsDistinctVolume: false, and
        //     the head must therefore come out on EXACTLY the torso's colour: same value, and by
        //     Mat's per-colour cache the same material instance, so nothing can drift them apart.
        //
        // BODY-1 INVERTED THIS PAIR, and the inversion is the honest form of it. The gumdrop
        // declared HeadIsDistinctVolume: FALSE (its head was the top slice of one revolved
        // profile), so the check read "declared false => head takes the torso's exact colour" with
        // a forced-true control. boxkid's head is a genuinely separate box and its row declares
        // TRUE, so the same two claims are asserted with the roles swapped: declared true => the
        // values must SEPARATE, and the forced-false control must collapse them back together.
        // Same two directions, same guarantee that the value break is neither unconditional nor
        // deleted — only the row it is demonstrated on changed.
        Color? headAlbedo = visual.PartAlbedo("Head");
        Color? torsoAlbedo = visual.PartAlbedo("Torso");
        GD.Print($"[sandbox] head value break: row declares distinct volume " +
                 $"{AvatarVisual.HeadIsDistinctVolumeFor(key)}; head {headAlbedo}, torso {torsoAlbedo}");
        Check("a_head_that_is_a_distinct_volume_is_separated_by_value",
            AvatarVisual.HeadIsDistinctVolumeFor(key)
            && headAlbedo != null && torsoAlbedo != null
            && !headAlbedo.Value.IsEqualApprox(torsoAlbedo.Value));

        // 3c. ITS POSITIVE CONTROL, and the reason this pair exists rather than the check alone:
        //     "head == torso" passes just as happily if the value break were DELETED as if it were
        //     made conditional, and deleting it would silently strip ART-BIBLE §3's separation from
        //     the first model that ships a real head volume. Force the declaration true and the two
        //     colours must diverge again.
        AvatarVisual.OverrideHeadIsDistinctVolumeForTest(key, false);
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);
        Color? brokenHead = visual.PartAlbedo("Head");
        Color? brokenTorso = visual.PartAlbedo("Torso");
        GD.Print($"[sandbox] head value break, forced OFF: head {brokenHead}, torso {brokenTorso}");
        Check("declaring_the_head_one_volume_takes_the_torsos_exact_colour",
            brokenHead != null && brokenTorso != null
            && brokenHead.Value.IsEqualApprox(brokenTorso.Value));
        AvatarVisual.OverrideHeadIsDistinctVolumeForTest(key, true);
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);

        // 2b. THE POSITIVE CONTROL. Declare nothing absent and every part the file really lacks must
        //     become loud again — including all eight, which is also the proof that none of the eight
        //     was declared to hide a part that is actually there.
        var empty = new System.Collections.Generic.HashSet<string>();
        AvatarVisual.OverrideDeclaredAbsentPartsForTest(key, empty);
        AvatarVisual.ResetUndeclaredMissingParts();
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);
        var seen = new System.Collections.Generic.HashSet<string>(AvatarVisual.UndeclaredMissingParts);
        GD.Print($"[sandbox] positive control, nothing declared absent: {seen.Count} undeclared " +
                 $"missing part(s) [{string.Join(", ", seen)}]");
        Check("an_undeclared_missing_part_still_errors",
            seen.Count == declared.Count && seen.SetEquals(declared));
        AvatarVisual.OverrideDeclaredAbsentPartsForTest(key, declared);

        // 4. THE FALLBACK, forced.
        AvatarVisual.ForceModelLoadFailure(key, true);
        AvatarVisual.ResetUndeclaredMissingParts();
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);
        int meshes = CountRealMeshes(visual);
        GD.Print($"[sandbox] forced load failure: '{visual.ResolvedModelPath}' " +
                 $"(from file: {visual.ResolvedFromFile}), crown {visual.BodyBounds.End.Y:F4} m, " +
                 $"eye {(visual.EyeCentre?.Y ?? -1f):F4} m, {meshes} real mesh(es), " +
                 $"knees {visual.HasKnees}, elbows {visual.HasElbows}, waist {visual.HasWaist}, " +
                 $"{AvatarVisual.UndeclaredMissingParts.Count} undeclared missing");
        Check("a_failed_model_load_falls_back_to_the_primitive",
            !visual.ResolvedFromFile
            && visual.ResolvedModelPath == GreyboxAvatarBody.SourceLabel
            && visual.AvatarKey == key);
        // ...and the fallback RENDERS. A fallback that swaps in an empty rig is worse than a crash,
        // because it looks like it worked. Real meshes, standing at the right height, joints intact.
        Check("the_fallback_body_actually_renders",
            meshes >= 10
            && Mathf.Abs(visual.BodyBounds.End.Y - GreyboxAvatarBody.CrownM) <= 0.005f
            && visual.HasKnees && visual.HasElbows && visual.HasWaist
            && AvatarVisual.UndeclaredMissingParts.Count == 0);

        // Back to the file, and prove the recovery — otherwise the seam could be leaving every
        // later avatar in the process on the fallback and nothing here would notice.
        AvatarVisual.ForceModelLoadFailure(key, false);
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
        await Ticks(2);
        Check("clearing_the_forced_failure_returns_to_the_file",
            visual.ResolvedFromFile && visual.ResolvedModelPath.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase));

        // --- 5. THE PLAYED BODY --------------------------------------------------------------
        //
        // BODY-1 (2026-08-28) added this block because everything above it was about a body that
        // was NOT the one a player got, so the suite had nothing at all to say about the body a
        // player actually IS — which is precisely the hole that let two consecutive attempts ship
        // the wrong one.
        //
        // BODY-2 (2026-08-28) made the two the same body and KEPT THE BLOCK ANYWAY, which is the
        // decision worth defending. Everything above asserts an authored-asset contract and would
        // stay green if PreferredAvatarKey were repointed at another body tomorrow; only this block
        // asserts that what the picker's row and the world's fallback produce is the body Talon
        // asked for. The two agreeing today is a fact to check, not a reason to stop checking.
        //
        // It also flipped every polarity here: the played body is loaded FROM A FILE now, and
        // "from a file" is the real guarantee rather than an absence — Talon's standing "I don't
        // like assets of things that I can't open" ruling is what this line defends.
        var playedCarrier = new Node3D { Name = "PlayedBodyRig", Position = new Vector3(260, 40, 240) };
        AddChild(playedCarrier);
        var played = new AvatarVisual { Name = "PlayedBody" };
        playedCarrier.AddChild(played);
        AvatarVisual.ResetUndeclaredMissingParts();
        played.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), AvatarVisual.PreferredAvatarKey);
        await Ticks(2);
        int playedMeshes = CountRealMeshes(played);
        GD.Print($"[sandbox] played body: key '{AvatarVisual.PreferredAvatarKey}' -> " +
                 $"'{played.ResolvedModelPath}' (from file: {played.ResolvedFromFile}), " +
                 $"crown {played.BodyBounds.End.Y:F4} m, eye {(played.EyeCentre?.Y ?? -1f):F4} m, " +
                 $"{playedMeshes} real mesh(es), knees {played.HasKnees}, elbows {played.HasElbows}, " +
                 $"waist {played.HasWaist}, {AvatarVisual.UndeclaredMissingParts.Count} undeclared missing");
        Check("the_played_body_is_the_box_kid_and_is_loaded_from_a_file",
            AvatarVisual.PreferredAvatarKey == AvatarVisual.BoxKidAvatarKey
            && played.ResolvedFromFile
            && played.ResolvedModelPath == AvatarVisual.BoxKidModelPath
            && ResourceLoader.Exists(played.ResolvedModelPath)
            && played.AvatarKey == AvatarVisual.PreferredAvatarKey);
        // ...and it RENDERS, with the rig the animation layer drives. Same argument as the
        // fallback's own render check: a body that resolves correctly and draws nothing looks
        // exactly like a body that works, right up until someone opens the game.
        Check("the_played_body_actually_renders_with_a_full_rig",
            playedMeshes >= 10
            && played.HasKnees && played.HasElbows && played.HasWaist
            && played.EyeCentre != null
            && AvatarVisual.UndeclaredMissingParts.Count == 0);
        // The picker's default row and the played body must be the same thing. This is the bug
        // BODY-1 fixed, asserted where a live AvatarVisual can see it rather than only in xUnit.
        //
        // AVATAR-1 (2026-09-05) removed the picker from the shipped menus, so the mechanism this
        // guards is currently DORMANT rather than gone: HostMenu/JoinMenu no longer write
        // LocalAvatarKey at all, and an unset key resolves through ResolveEnvAvatarKey to
        // PreferredAvatarKey. The check is KEPT and not weakened, because the day the picker
        // comes back it will come back writing RosterEntries[_avatarIndex].Key into a value that
        // BEATS the world's preferred key, and this is the assertion that stops it returning
        // pointed at the wrong body. See OneBodyForTheFirstBuildTests for the absence half.
        Check("the_pickers_default_row_builds_the_played_body",
            AvatarVisual.RosterEntries[0].Key == AvatarVisual.PreferredAvatarKey);

        // --- 5b. THE CLASSIC GREYBOX STILL BUILDS ---------------------------------------------
        //
        // BODY-2 scope item 1: greybox_classic stays on the roster and is not deleted. A roster row
        // nothing selects is a row that rots quietly, and this one has two live jobs — it is the
        // last code-built player-shaped body and the only control a value-ramp or silhouette claim
        // about the box kid can be measured against. So it is BUILT here rather than merely
        // asserted to be in a table: "the row exists" and "the row still produces a body" are
        // different claims, and only the second one is worth anything the day someone edits
        // ClassicGreyboxAvatarBody.
        var classicCarrier = new Node3D { Name = "ClassicGreyboxRig", Position = new Vector3(280, 40, 240) };
        AddChild(classicCarrier);
        var classic = new AvatarVisual { Name = "ClassicGreybox" };
        classicCarrier.AddChild(classic);
        AvatarVisual.ResetUndeclaredMissingParts();
        classic.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), AvatarVisual.ClassicGreyboxAvatarKey);
        await Ticks(2);
        int classicMeshes = CountRealMeshes(classic);
        GD.Print($"[sandbox] classic greybox (kept, not default): '{classic.ResolvedModelPath}' " +
                 $"(from file: {classic.ResolvedFromFile}), {classicMeshes} real mesh(es), " +
                 $"crown {classic.BodyBounds.End.Y:F4} m");
        Check("the_classic_greybox_survives_as_a_selectable_code_built_row",
            !classic.ResolvedFromFile
            && classicMeshes >= 10
            && classic.HasKnees && classic.HasElbows && classic.HasWaist
            && AvatarVisual.IsValidAvatarKey(AvatarVisual.ClassicGreyboxAvatarKey)
            && AvatarVisual.ClassicGreyboxAvatarKey != AvatarVisual.PreferredAvatarKey);
        classicCarrier.QueueFree();

        // --- 5c. THE VALUE RAMP, on the real meshes -------------------------------------------
        //
        // BODY-2 scope item 4. The CURVE is unit-tested engine-free (BoxKidAvatarTests); this is
        // the other half, and neither is sufficient alone — the pure tests would pass unchanged if
        // ApplyValueRamp were never called at all, and this one would pass on any monotonic curve.
        // What it measures is that the ramp REACHES THE MESHES: the feet come out darker than the
        // head on the body a player actually gets, and pushing the knob widens the gap.
        //
        // Value is read as Rec.601-ish luma off the MaterialOverride's albedo rather than as the
        // raw component, because the palette colours are not grey and comparing R to R across two
        // hues would measure the hue.
        var rampCarrier = new Node3D { Name = "ValueRampRig", Position = new Vector3(300, 40, 240) };
        AddChild(rampCarrier);
        var ramp = new AvatarVisual { Name = "ValueRamp" };
        rampCarrier.AddChild(ramp);
        var rampColor = new Color(0.42f, 0.68f, 0.34f);
        float spreadAtOne = 0f, spreadAtPushed = 0f;
        bool rampOrderHolds = true;
        try
        {
            foreach ((float strength, bool pushed) in new[] { (1f, false), (2.8f, true) })
            {
                AvatarVisual.BodyValueRampStrength = strength;
                ramp.BuildAppearance(rampColor, AvatarVisual.PreferredAvatarKey);
                await Ticks(2);
                float footLuma = PartLuma(ramp, "FootL");
                float headLuma = PartLuma(ramp, "Head");
                float spread = headLuma - footLuma;
                GD.Print($"[sandbox] value ramp at strength {strength:0.##}: " +
                         $"FootL luma {footLuma:F4}, Head luma {headLuma:F4}, spread {spread:F4}");
                rampOrderHolds &= footLuma >= 0f && headLuma >= 0f && spread > 0.01f;
                if (pushed) spreadAtPushed = spread; else spreadAtOne = spread;
            }
        }
        finally
        {
            // Restored whatever happens: a leaked strength would silently re-tint every body every
            // later section of this suite builds.
            AvatarVisual.ResetBodyValueRampStrength();
        }
        Check("the_value_ramp_darkens_the_feet_and_lightens_the_head", rampOrderHolds);
        Check("pushing_the_value_ramp_knob_widens_the_spread_on_the_real_body",
            spreadAtPushed > spreadAtOne + 0.01f);
        rampCarrier.QueueFree();

        playedCarrier.QueueFree();
        carrier.QueueFree();
        await Ticks(2);
    }

    /// <summary>Rec.601 luma of a named part's overridden albedo, or -1 when the part or its
    /// override is not there. Luma rather than a raw channel because the palette colours are not
    /// grey, and comparing one hue's red against another's would measure the hue rather than the
    /// value the ramp moves.</summary>
    private static float PartLuma(Node root, string partName)
    {
        if (root.FindChild(partName, recursive: true, owned: false) is not MeshInstance3D mesh)
            return -1f;
        if (mesh.MaterialOverride is not StandardMaterial3D mat)
            return -1f;
        Color c = mat.AlbedoColor;
        return 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
    }

    /// <summary>Meshes with actual geometry under a rig — declared-absent placeholders carry no
    /// <see cref="Mesh"/> and are deliberately not counted.</summary>
    private static int CountRealMeshes(Node node)
    {
        int n = node is MeshInstance3D { Mesh: not null } ? 1 : 0;
        foreach (Node child in node.GetChildren())
            n += CountRealMeshes(child);
        return n;
    }

    // --- Body proportions, across the WHOLE roster ----------------------------------------

    /// <summary>Everything the game sizes against the player's body, checked against the body
    /// it was sized against — for every character on the roster, not for the one that happened
    /// to be the player when the number was typed.
    ///
    /// <para>THE BUG THIS SUITE EXISTS FOR. Until 2026-08-08 the player was always one squat blob,
    /// and eight dimensions were tuned by eye against it. Whole figures became the player character
    /// and none of them moved: a 1.16–1.27 m kid inside a 0.9 m capsule (a head that passes
    /// through ceilings and takes no part in collision), an aim ray leaving from 20 cm below its
    /// eyes, a nameplate drawn across its face, and a shadow sized for something twice as
    /// wide. That is the repo's standing failure mode — a change made exactly as asked with the
    /// implications left unhandled — and it is only caught by a test that asks about the
    /// RELATIONSHIP between a number and a body.</para>
    ///
    /// <para>So nothing below asserts a literal dimension. Every check is a relationship — the
    /// capsule contains the model, the eyeline is in the head, the plate clears the crown —
    /// which stays true and stays meaningful when an artist reshapes a character. The literal
    /// regression pin this suite used to end on went with the creature it pinned.</para></summary>
    private async System.Threading.Tasks.Task RunProportionsTestsAsync()
    {
        // Every roster entry, both families. A test that only covered the default character
        // would have passed happily on the day this broke.
        var failures = new List<string>();
        bool contained = true, notWiderThanBody = true, eyesReal = true, eyesInHead = true;
        bool plateClears = true, shadowFits = true, carryInFront = true, focusInUpperBody = true;
        bool voiceFromTheFace = true;

        foreach ((string key, string _) in AvatarVisual.RosterEntries)
        {
            var probe = new SandboxAvatar
            {
                Name = $"Probe_{key}", AvatarKey = key, Position = new Vector3(200, 40, 200),
            };
            AddChild(probe);
            // Measured after the tree has ticked, never inside _Ready: several things in this
            // codebase (deferred QueueFrees, placeholder nodes) settle on the frame AFTER the
            // one that created them, and a frame-zero reading can legitimately disagree.
            await Ticks(2);

            Aabb body = probe.Visual.BodyBounds;
            AvatarProportions p = probe.Proportions;
            var capsule = (CapsuleShape3D)probe.GetNode<CollisionShape3D>("Collision").Shape;
            float capsuleTop = probe.GetNode<CollisionShape3D>("Collision").Position.Y + capsule.Height * 0.5f;
            float capsuleBottom = probe.GetNode<CollisionShape3D>("Collision").Position.Y - capsule.Height * 0.5f;
            float widest = Mathf.Max(body.Size.X, body.Size.Z) * 0.5f;
            float frontDepth = Mathf.Abs(body.Position.Z); // -Z is forward; front face of the body
            float aimLocalY = probe.AimOriginGlobalPosition.Y - probe.GlobalPosition.Y;
            float voiceLocalY = probe.VoiceOriginGlobalPosition.Y - probe.GlobalPosition.Y;

            // 1. THE HEADLINE DEFECT. Nothing the player can see may stick out of the collider,
            //    and the collider may not start above the ground the avatar stands on.
            bool ok = capsuleTop >= body.End.Y - 0.001f && capsuleBottom <= 0.001f;
            contained &= ok;
            if (!ok)
                failures.Add($"{key}: capsule {capsuleBottom:F3}..{capsuleTop:F3} vs crown {body.End.Y:F3}");

            // 2. ...and equally, the collider may not be FATTER than the character. A capsule
            //    drawn round the widest cosmetic (the original blob's tail reached 0.57 m) makes a
            //    character that visibly fits a gap refuse to walk through it.
            ok = capsule.Radius <= widest + 0.001f && capsule.Radius > 0.1f;
            notWiderThanBody &= ok;
            if (!ok)
                failures.Add($"{key}: capsule r={capsule.Radius:F3} vs widest {widest:F3}");

            // 3. The aim ray leaves from this character's OWN eyes — measured off its eye
            //    geometry, not a ratio that happens to look plausible. Both halves matter: a
            //    model whose eyes silently failed to load would still produce a "plausible"
            //    eyeline from the fallback ratio and pass a check that only tested plausibility.
            eyesReal &= p.EyesMeasured;
            ok = aimLocalY > body.End.Y * 0.5f && aimLocalY < body.End.Y;
            eyesInHead &= ok;
            if (!ok)
                failures.Add($"{key}: eyeline {aimLocalY:F3} outside the head (crown {body.End.Y:F3})");

            // 4. The voice leaves from the face too — not from a metre of air above the head,
            //    which is where an absolute 1.6 m offset put every speaker in the game.
            voiceFromTheFace &= Mathf.Abs(voiceLocalY - aimLocalY) < 0.001f;

            // 5. The nameplate sits in a small gap OVER the head. Below the crown it is drawn
            //    across the character's face; far above it, it belongs to nobody.
            ok = p.NameplateHeightM > body.End.Y && p.NameplateHeightM < body.End.Y + 0.4f;
            plateClears &= ok;
            if (!ok)
                failures.Add($"{key}: nameplate {p.NameplateHeightM:F3} vs crown {body.End.Y:F3}");

            // 6. The shadow reads the silhouette: wider than the collider (which is deliberately
            //    tucked inside the body) but never wider than the character's own footprint.
            ok = p.ShadowRadiusM > capsule.Radius && p.ShadowRadiusM <= widest + 0.001f;
            shadowFits &= ok;
            if (!ok)
                failures.Add($"{key}: shadow {p.ShadowRadiusM:F3} vs r={capsule.Radius:F3}/widest {widest:F3}");

            // 7. A held item rides clear of the chest: well forward of the body's mid-line, and
            //    within arm's length of its front face rather than floating out in space.
            //    NOT "outside the front face" — the long-snouted members of the original
            //    creature family reached 0.62–0.67 m forward and the SHIPPED anchor was already inside
            //    that snout. That is a pre-existing cosmetic quirk of the placeholder creature
            //    family, and a test that demanded otherwise would be asserting something the
            //    game has never done.
            Vector3 carry = p.CarryAnchorRestLocal;
            ok = carry.Z < -frontDepth * 0.5f && carry.Z > -(frontDepth + 0.35f)
                && carry.Y > 0f && carry.Y < body.End.Y;
            carryInFront &= ok;
            if (!ok)
                failures.Add($"{key}: carry {carry} vs front {frontDepth:F3}, crown {body.End.Y:F3}");

            // 8. The follow camera frames the upper body, not the belt.
            ok = p.CameraFocusHeightM > body.End.Y * 0.5f && p.CameraFocusHeightM < body.End.Y;
            focusInUpperBody &= ok;
            if (!ok)
                failures.Add($"{key}: camera focus {p.CameraFocusHeightM:F3} vs crown {body.End.Y:F3}");

            probe.QueueFree();
            await Ticks(1);
        }

        foreach (string failure in failures)
            GD.Print($"SANDBOX-TEST   proportions detail -> {failure}");

        Check("proportions_capsule_contains_every_roster_model", contained);
        Check("proportions_capsule_never_wider_than_the_body", notWiderThanBody);
        Check("proportions_every_roster_model_has_measured_eyes", eyesReal);
        Check("proportions_aim_origin_is_inside_the_head", eyesInHead);
        Check("proportions_voice_leaves_from_the_face", voiceFromTheFace);
        Check("proportions_nameplate_clears_the_crown", plateClears);
        Check("proportions_shadow_inside_the_footprint", shadowFits);
        Check("proportions_carry_anchor_clears_the_chest", carryInFront);
        Check("proportions_camera_frames_the_upper_body", focusInUpperBody);

        // THE CASCADE ITSELF. A character can change identity mid-session (the avatar-key
        // synchronizer catching up to the authority's real pick a beat after spawn), and the
        // failure this whole packet is about is a body that changes while the numbers around it
        // do not. Swap one roster body for another in place and watch the collider follow — the
        // derived struct AND the actual CollisionShape3D node, which is the half a rebuild that
        // only recomputed numbers would leave behind.
        var swapped = new SandboxAvatar
        {
            Name = "ProbeSwap", AvatarKey = AvatarVisual.ClassicGreyboxAvatarKey,
            Position = new Vector3(220, 40, 200),
        };
        AddChild(swapped);
        await Ticks(2);
        swapped.AvatarKey = AvatarVisual.BoxKidAvatarKey;
        await Ticks(2);
        var rebuiltCapsule = (CapsuleShape3D)swapped.GetNode<CollisionShape3D>("Collision").Shape;
        Check("proportions_rebuild_resizes_the_body",
            swapped.Visual.AvatarKey == AvatarVisual.BoxKidAvatarKey
            && Mathf.Abs(rebuiltCapsule.Height - swapped.Proportions.CapsuleHeightM) < 1e-4f
            && Mathf.Abs(rebuiltCapsule.Radius - swapped.Proportions.CapsuleRadiusM) < 1e-4f);

        swapped.QueueFree();
        await Ticks(2);
    }

    /// <summary>
    /// <b>Measures foot slip in world space, in the engine, at three steady gears.</b> MOVE-1's
    /// definition of "the feet are planted", and the one number a capture cannot certify.
    ///
    /// <para>Sampled only across frames where the SAME foot is in stance on both the previous and
    /// the current frame — a stance boundary is a hand-off, not a slip, and counting it would report
    /// the stride length as an error.</para>
    /// </summary>
    /// <summary>
    /// <b>The authored clip library, driven through the production path and measured.</b>
    ///
    /// <para>Six claims, each of which has a way of failing that looks like something else:</para>
    /// <list type="number">
    /// <item><b>The tree builds.</b> A missing <c>AnimationPlayer</c> or a renamed clip is a body
    /// that resolves every joint and stands perfectly still.</item>
    /// <item><b>The clips MOVE the rig.</b> A resolved track can still be flat, and a flat track is
    /// indistinguishable from a clip that was keyed wrong.</item>
    /// <item><b>The upper-body override does not freeze the legs.</b> ANIM-M2 S7's finding: Godot's
    /// importer pads every clip out to six tracks, so an unfiltered blend of a two-channel arm
    /// override drives the LEG tracks with the override's padding. This is the track filter, tested.</item>
    /// <item><b>Two independently built bodies fed the same replicated state converge.</b> The parity
    /// law applied to the pose. Nothing animation-shaped is on the wire, so this has to fall out of
    /// the arithmetic or it does not hold at all.</item>
    /// <item><b>A body that arrives mid-action lands mid-clip.</b> The brief's late-join case,
    /// measured against a body that watched the whole thing happen.</item>
    /// <item><b>LimbIk still owns the knee.</b> The clip proposes a hip angle and the solver
    /// disposes; a knee stuck at zero across a whole walk means the clip took the node.</item>
    /// </list>
    ///
    /// <para><b>The flag is set and restored around this block</b>, so the rest of the suite keeps
    /// measuring the shipped procedural gait - which is what the foot-slip canary above is
    /// asserting against. (<c>Run-BodyLanguageTest</c> asserted it too until ANIM-M2b deleted that
    /// family under ruling 9.)</para>
    /// </summary>
    private async System.Threading.Tasks.Task RunAuthoredClipTestsAsync()
    {
        bool previous = Anim.AvatarClipFlag.AuthoredClips;
        Anim.AvatarClipFlag.AuthoredClips = true;
        try
        {
            // BODY-1 (2026-08-28): the AUTHORED key. The clip library is built OVER an authored
            // rig — IsAuthoredRig(key) is asserted two lines down — and the played body is now
            // code-built (greybox_classic, Build.PrimitiveParts), which ships no clips at all and
            // leaves ClipsDriving false. Same reasoning as RunAuthoredBodyTestsAsync: aim the
            // authored-pipeline test at the authored body rather than dilute what it asserts.
            string key = AvatarVisual.BoxKidAvatarKey;
            var carrier = new Node3D { Name = "ClipRig", Position = new Vector3(300, 40, 300) };
            AddChild(carrier);
            var bodyA = new AvatarVisual { Name = "ClipBodyA" };
            carrier.AddChild(bodyA);
            bodyA.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            var bodyB = new AvatarVisual { Name = "ClipBodyB" };
            carrier.AddChild(bodyB);
            bodyB.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            await Ticks(2);

            GD.Print($"[sandbox] clip layer: driving {bodyA.ClipsDriving}, row is authored rig " +
                     $"{AvatarVisual.IsAuthoredRig(key)}, facing mirror " +
                     $"{AvatarVisual.AppliesFacingMirrorFor(key)}");
            Check("clip_library_builds_over_the_authored_rig",
                bodyA.ClipsDriving && bodyB.ClipsDriving
                && AvatarVisual.IsAuthoredRig(key)
                && !AvatarVisual.AppliesFacingMirrorFor(key));

            // 2. THE CLIPS MOVE THE RIG. Walked at the Walk clip's own nominal speed, so the warp is
            //    ~1.0 and what is measured is the authored arc rather than a stretched copy of it.
            const float dt = MpFoundation.Net.AvatarMotor.TickDelta;
            var walk = new Vector3(0f, 0f, -Anim.ClipTimeWarp.WalkNominalMps);
            float minKnee = float.MaxValue, maxKnee = float.MinValue;
            float minLeg = float.MaxValue, maxLeg = float.MinValue;
            for (int i = 0; i < 120; i++)
            {
                bodyA.SetNetworkTick(i);
                bodyA.Animate(dt, walk, onFloor: true);
                float leg = bodyA.HipPitchRad(left: true);
                minLeg = Mathf.Min(minLeg, leg);
                maxLeg = Mathf.Max(maxLeg, leg);
                minKnee = Mathf.Min(minKnee, bodyA.KneeBendRad(left: true));
                maxKnee = Mathf.Max(maxKnee, bodyA.KneeBendRad(left: true));
            }
            float legSweepDeg = Mathf.RadToDeg(maxLeg - minLeg);
            GD.Print($"[sandbox] clip layer: Walk drove the left hip through {legSweepDeg:F2} deg " +
                     $"over two seconds; cadence {bodyA.CadenceHz:F2} steps/s, duty {bodyA.DutyFactor:F3}");
            // The authored stance runs +38.7 to -38.7 deg with a swing overshoot past both, so a live
            // Walk must sweep well past 60 deg. A flat or unresolved track sweeps about zero.
            Check("authored_clips_actually_move_the_rig", legSweepDeg > 60f);

            // The cadence readout re-sourced from the clip rather than left on the procedural gait -
            // it is what _gaitPhase integrates and therefore what the footstep latch fires on.
            //
            // MOVE-8: THE EXPECTATION IS DERIVED, AND THE NEGATIVE CONTROL IS WHAT MAKES IT A TEST.
            // This read `CadenceHz == 2.5 && DutyFactor == WalkDutyFactor`, and both literals were
            // right only while the Walk clip's nominal speed happened to sit inside the WALK gear's
            // band. Talon's speed ruling moved the band out from under it: JogEnterMps is
            // WalkSpeedMps * 1.12, which fell 2.72 -> 1.92, so driving at the clip's 2.4207 m/s
            // nominal now resolves to JOG and the readout correctly reports the Run clip's warped
            // cadence instead. That is the same finding ClipTimeWarpTests carries as MOVE-8's
            // fork 3 (the walk clip no longer anchors the walk gear), showing up a second time.
            //
            // So the expectation comes off the gear the body actually resolved. On its own that
            // would be near-tautological, which is why the second clause is the load-bearing one:
            // the readout must NOT be the procedural LocomotionProfile.CadenceAt value, and that is
            // precisely what "re-sourced from the clip" claims. The two differ by a factor of
            // several at every gear, so the check has real separation rather than a tolerance.
            float clipCadence = Anim.ClipTimeWarp.CadenceHzFor(bodyA.Gear, Anim.ClipTimeWarp.WalkNominalMps);
            float proceduralCadence = LocomotionProfile.CadenceAt(Anim.ClipTimeWarp.WalkNominalMps);
            GD.Print($"[sandbox] clip layer: gear {bodyA.Gear} at the Walk clip's nominal " +
                     $"{Anim.ClipTimeWarp.WalkNominalMps:F4} m/s; clip cadence {clipCadence:F3} Hz " +
                     $"vs procedural {proceduralCadence:F3} Hz");
            Check("the_cadence_readout_comes_from_the_warped_clip",
                Mathf.Abs(bodyA.CadenceHz - clipCadence) < 0.05f
                && Mathf.Abs(bodyA.DutyFactor - Anim.ClipTimeWarp.DutyFactorFor(bodyA.Gear)) < 0.01f
                && Mathf.Abs(bodyA.CadenceHz - proceduralCadence) > 0.25f);

            // 6. LimbIk still owns the knee: the solver is handed the clip's hip angle and answers
            //    with a bend, exactly as it answered the derived gait's.
            GD.Print($"[sandbox] clip layer: knee bend ranged {Mathf.RadToDeg(minKnee):F2} to " +
                     $"{Mathf.RadToDeg(maxKnee):F2} deg under the clip-driven gait");
            Check("limbik_still_owns_the_knee_under_the_clip_layer",
                float.IsFinite(minKnee) && float.IsFinite(maxKnee) && maxKnee >= 0f);

            // 3. THE TRACK FILTER. An arm override must not touch the legs. Driven for two seconds
            //    with a carry engaged and the leg sweep re-measured: if the padded rest tracks were
            //    reaching the legs, they would go flat.
            // MERGE NOTE (PLAYTEST-1 trunk): ANIM-M3 added these two calls; CARRY-1 deleted
            // SetCarrying(bool) in favour of SetCarry(CarryPose). Armful is the exact
            // equivalent of the old `true` — it is the both-arms-occupied pose the old bool
            // meant, and it is what keeps this check measuring an upper-body override.
            bodyB.SetCarry(CarryPose.Armful);
            float minHeld = float.MaxValue, maxHeld = float.MinValue;
            for (int i = 0; i < 120; i++)
            {
                bodyB.SetNetworkTick(i);
                bodyB.Animate(dt, walk, onFloor: true);
                minHeld = Mathf.Min(minHeld, bodyB.HipPitchRad(left: true));
                maxHeld = Mathf.Max(maxHeld, bodyB.HipPitchRad(left: true));
            }
            float heldSweepDeg = Mathf.RadToDeg(maxHeld - minHeld);
            GD.Print($"[sandbox] clip layer: with an upper-body override layered, the left hip still " +
                     $"swept {heldSweepDeg:F2} deg (unlayered {legSweepDeg:F2} deg) - the track " +
                     "filter is what makes this an override rather than a full-body cycle");
            Check("the_upper_body_override_does_not_freeze_the_legs", heldSweepDeg > 60f);
            bodyB.SetCarry(CarryPose.None);

            // 4. PARITY. Two independently built bodies, the same replicated state, the same frame.
            //    Nothing animation-shaped crossed between them, so agreement can only come from the
            //    arithmetic - which is the whole claim.
            var bodyC = new AvatarVisual { Name = "ClipBodyC" };
            carrier.AddChild(bodyC);
            bodyC.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            var bodyD = new AvatarVisual { Name = "ClipBodyD" };
            carrier.AddChild(bodyD);
            bodyD.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            await Ticks(2);
            for (int i = 0; i < 90; i++)
            {
                bodyC.SetNetworkTick(i);
                bodyC.Animate(dt, walk, onFloor: true);
                bodyD.SetNetworkTick(i);
                bodyD.Animate(dt, walk, onFloor: true);
            }
            float parityDeg = Mathf.RadToDeg(Mathf.Abs(bodyC.HipPitchRad(left: true) - bodyD.HipPitchRad(left: true)));
            GD.Print($"[sandbox] clip layer parity: two bodies fed the same replicated state differ " +
                     $"by {parityDeg:F4} deg at the hip");
            Check("two_bodies_on_the_same_replicated_state_render_the_same_pose", parityDeg < 0.01f);

            // 5. THE MID-ACTION JOIN. A body that has been down for a second and a half, and a body
            //    that has only just been told about it, must show the same frame - the second one
            //    must not play the fall it missed. That is the tick anchor plus the first-sight rule.
            var watched = new AvatarVisual { Name = "ClipBodyWatched" };
            carrier.AddChild(watched);
            watched.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            await Ticks(2);
            watched.SetIncapacity(Sail.Game.Failure.IncapacityState.KnockedOut, 1.2f, 0.3f);
            for (int i = 0; i < 90; i++)
            {
                watched.SetNetworkTick(i);
                watched.Animate(dt, Vector3.Zero, onFloor: true);
            }
            var joined = new AvatarVisual { Name = "ClipBodyJoined" };
            carrier.AddChild(joined);
            joined.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), key);
            await Ticks(2);
            joined.SetIncapacity(Sail.Game.Failure.IncapacityState.KnockedOut, 1.2f, 0.3f);
            joined.SetNetworkTick(89);
            joined.Animate(dt, Vector3.Zero, onFloor: true);
            float joinDeg = Mathf.RadToDeg(Mathf.Abs(watched.ShoulderPitchRad(left: true) - joined.ShoulderPitchRad(left: true)));
            GD.Print($"[sandbox] clip layer mid-action join: a body told about a knock-out on its " +
                     $"first frame differs from one that watched it happen by {joinDeg:F3} deg at " +
                     "the shoulder");
            Check("a_body_that_arrives_mid_action_lands_mid_clip", joinDeg < 0.5f);
        }
        finally
        {
            Anim.AvatarClipFlag.AuthoredClips = previous;
        }
    }

    private async System.Threading.Tasks.Task RunFootSlipTestAsync()
    {
        // MEASURED ON A GREYBOX, NOT ON THIS SUITE'S OWN AVATAR, and that is a correction rather
        // than a convenience. The first cut drove the shared test avatar — a squat blob whose foot
        // node sits 0.08 m off the ground. On an 0.08 m leg the derived stance is narrower than a
        // physics frame, so the walk collected ZERO usable samples and the check failed having
        // measured nothing. (It also surfaced a real defect while doing so — see
        // LocomotionProfile.DutyFactorAt's doc and StanceFootDoesNotSlip_AtEveryLegLength.)
        //
        // So this builds its own AvatarVisual on the greybox key and drives Animate directly, the
        // way GreyboxPlayerLab does — the same production code, on the body the player is, at a
        // speed the motor can actually produce. Nothing is simulated: the carrier is translated by
        // exactly v*dt each tick and the visual is handed exactly that velocity, so any horizontal
        // movement of a planted contact point is slip and nothing else.
        var carrier = new Node3D { Name = "FootSlipRig" };
        AddChild(carrier);
        var visual = new AvatarVisual { Name = "FootSlipVisual" };
        carrier.AddChild(visual);
        // BODY-1: the constant, not the literal. This gate exists to measure the body the PLAYER
        // is, and a literal here is how it would have gone on measuring a retired one.
        visual.BuildAppearance(new Color(0.6f, 0.7f, 0.65f), AvatarVisual.PreferredAvatarKey);
        await Ticks(2);

        (string Label, float Speed)[] gears =
        {
            ("walk", LocomotionProfile.WalkSpeedMps),
            ("jog", LocomotionProfile.JogSpeedMps),
            ("sprint", LocomotionProfile.SprintSpeedMps),
        };

        const float dt = MpFoundation.Net.AvatarMotor.TickDelta;
        float worstOverall = 0f;
        foreach ((string label, float speed) in gears)
        {
            var localVel = new Vector3(0f, 0f, -speed);
            for (int i = 0; i < 40; i++) // let the gait amplitude and the gear settle
            {
                carrier.Position += new Vector3(0f, 0f, -speed * dt);
                visual.Animate(dt, localVel, onFloor: true, sprinting: speed > LocomotionProfile.JogSpeedMps);
                await Ticks(1);
            }

            float worst = 0f;
            int samples = 0;
            Vector3 prevL = visual.FootContactGlobal(left: true);
            Vector3 prevR = visual.FootContactGlobal(left: false);
            bool wasL = visual.FootInStance(left: true);
            bool wasR = visual.FootInStance(left: false);
            for (int i = 0; i < 240; i++)
            {
                carrier.Position += new Vector3(0f, 0f, -speed * dt);
                visual.Animate(dt, localVel, onFloor: true, sprinting: speed > LocomotionProfile.JogSpeedMps);
                await Ticks(1);

                Vector3 nowL = visual.FootContactGlobal(left: true);
                Vector3 nowR = visual.FootContactGlobal(left: false);
                bool inL = visual.FootInStance(left: true);
                bool inR = visual.FootInStance(left: false);
                // Horizontal only: a planted foot may ride the terrain up and down, and the claim
                // being tested is that it does not slide along the ground.
                if (inL && wasL)
                {
                    worst = Mathf.Max(worst, new Vector2(nowL.X - prevL.X, nowL.Z - prevL.Z).Length());
                    samples++;
                }
                if (inR && wasR)
                {
                    worst = Mathf.Max(worst, new Vector2(nowR.X - prevR.X, nowR.Z - prevR.Z).Length());
                    samples++;
                }
                prevL = nowL;
                prevR = nowR;
                wasL = inL;
                wasR = inR;
            }

            float perFrame = speed * dt;
            GD.Print($"[sandbox] foot slip, {label}: worst {worst * 1000f:F3} mm/frame over {samples} " +
                     $"stance samples at {speed:F2} m/s ({visual.Gear}, {visual.CadenceHz:F2} " +
                     $"steps/s, stride {LocomotionProfile.StepLengthAt(speed):F3} m, reach " +
                     $"{visual.StanceReachM:F3} m, duty {visual.DutyFactor:F3}, leg {visual.LegLengthM:F3} m) " +
                     $"— a foot that did not track the ground at all would slip {perFrame * 1000f:F1} mm/frame");
            // BOTH halves, and the sample count is not a formality: a check that passes because it
            // collected nothing is the shape this repo's own rules file warns about.
            Check($"gait_stance_was_sampled_{label}", samples > 10);
            Check($"gait_stance_foot_is_planted_{label}", worst < perFrame * 0.05f);
            worstOverall = Mathf.Max(worstOverall, worst);
        }

        GD.Print($"[sandbox] worst foot slip across every gear: {worstOverall * 1000f:F3} mm/frame");
        carrier.QueueFree();
        await Ticks(2);
    }

    private async System.Threading.Tasks.Task Ticks(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Every pose quantity RIG-1's solver can write, checked for finiteness (RIG-1). A NaN
    /// reaching a <c>Transform3D</c> is how a body silently vanishes — Godot declines to draw the mesh
    /// and nothing logs a word — so this is the positive control for <c>LimbIk</c>'s clamps on a live
    /// rig rather than only in its unit tests.</summary>
    private static bool FiniteRig(AvatarVisual v) =>
        float.IsFinite(v.BodyTiltX)
        && float.IsFinite(v.BodyOffsetY)
        && float.IsFinite(v.BodyYaw)
        && float.IsFinite(v.WaistPitchRad)
        && float.IsFinite(v.CrouchDropM)
        && float.IsFinite(v.KneeBendRad(left: true))
        && float.IsFinite(v.KneeBendRad(left: false))
        && float.IsFinite(v.ElbowBendRad(left: true))
        && float.IsFinite(v.ElbowBendRad(left: false))
        && v.FootContactGlobal(left: true).IsFinite()
        && v.FootContactGlobal(left: false).IsFinite()
        && v.PoseGlobalTransform.Origin.IsFinite();

    // --- Reporting --------------------------------------------------------------------

    private void Check(string name, bool ok)
    {
        _results.Add((name, ok));
        GD.Print($"SANDBOX-TEST {name}: {(ok ? "PASS" : "FAIL")}");
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        int failed = _results.FindAll(r => !r.Ok).Count;
        GD.Print($"SANDBOX-TEST OVERALL: {(failed == 0 ? "PASS" : "FAIL")} ({_results.Count - failed}/{_results.Count})");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
