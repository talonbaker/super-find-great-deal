# The animation contract

**What this is.** The runtime half of the procedural→authored migration: who owns which channel,
what a clip's time is anchored to, what a marker may and may not decide, and what nothing is allowed
to put on the wire. Written so the next agent does not re-open any of it.

**Why here and not in `BLENDER-EXPORT.md` (the packet asked which, and this is the answer).**
`BLENDER-EXPORT.md` is the **authoring** contract — a checklist somebody follows in Blender before
pressing export: bone names, uniform scale, facing, the clip naming pattern, what may be keyed. Every
item in it is checkable inside Blender. Everything below is about **runtime**: replication, tick
clocks, blend modes, LOD tiers, which of two writers owns a component. None of it can be checked in
Blender and none of it changes what an artist does. Folding a networking contract into an export
checklist would make both harder to follow and would put the parity law somewhere nobody reading
about the parity law would think to look. `BLENDER-EXPORT.md` §"Animation clips" now points here for
the runtime half, and this file points back for the authoring half.

Status: **ANIM-M3, 2026-08-21.** The clip layer ships behind `--authored-clips`, default OFF — see
§8.

---

## 1. Position authority — decided, not open

> **Position, velocity, gait/gear, facing, hold-state and action-state stay server-authoritative
> replicated state. Animation is a pure function of that state plus local time. No animation state is
> ever replicated.**

This is the parity law (canon §0.9) applied to motion: gameplay-relevant state is server data,
identical on every client; rendering is presentation only. Concretely:

- **Root motion never moves the body.** Clips are authored with the root locked. Root-motion
  extraction is an editor/tool measurement only: it measures a clip's stride, that number is written
  into the locomotion constants (`ClipTimeWarp.WalkNominalMps` / `RunNominalMps`), and it never
  produces a runtime position delta. If a stride and a speed disagree, one of the two numbers
  changes — two systems never fight over the transform.
- **The `AnimationTree` reads replicated state and owns none.** Its inputs are horizontal speed,
  gear, grounded/airborne, vertical velocity, hold-state and the skid timer. Every one of those is
  already on the wire or is a pure function of something that is.
- **No field was added to `MoveState` or `NetCodec.Snapshot` by this work**, and none should be. If a
  future state genuinely needs an entry tick on the wire, that is a codec change with its own packet,
  not a line added to an animation file.

**The derived-not-replicated rule, restated because it is the thing that drifts.** `Gear`,
`AvatarActionState` and `AvatarHoldState` are all *derived* on each client by pure functions
(`LocomotionProfile.GearFor`, `AvatarActionStates.Derive`, `AvatarVisual.DeriveHoldState`). A field
for any of them would be a second, contradictable copy of facts the peer already has, and it would
let a client assert a pose.

## 2. Clip-time anchoring

A client that receives a state change late, or joins during one, must land **mid-clip at the right
frame** — never at frame 0, and never at the frame it would reach if the action started now.

Three sources, in order of preference. Where a better one exists it is used.

| Source | Used for | Why |
|---|---|---|
| **Replicated progress** — `NetSwingState.Progress01` | the net stroke (`Hold_NetSwing`) | The authority recomputes it every tick and drives it onto every peer, so a joining client is correct on its **first frame** rather than on its first observed transition. It cannot drift, because it is never integrated locally. |
| **Replicated remaining** — `MoveState.SkidRemaining` | the brake (`Skid`) | Simulated identically by the server, owner prediction and replay, so every peer can compute how far into the brake a body is without being told. Backdated as `SkidMaxSec − remaining`. |
| **`(now − entryTick)`** in the server's tick clock | everything else | The general case. `now` is `SandboxAvatar.RenderTickNow()`; the entry tick is stamped locally on the frame the derived state changes. |

**First sight is not an entry.** A client that spawns a body which is *already* knocked out did not
watch it fall; stamping "now" would play the fall from frame 0 and render a body standing up in order
to collapse again. A state observed as already-true on a body's first frame is entered **at its end**
— which, for a one-shot that holds its last frame, is exactly the pose the body has been in since
before this client existed. Measured: 0.000° of shoulder difference between a body that watched a
knock-out and one told about it on its first frame (`SandboxSelfTest`,
`a_body_that_arrives_mid_action_lands_mid_clip`).

**Which tick is "now" is four different quantities**, and `SandboxAvatar.RenderTickNow()` resolves
them: the server's own counter on the authority; last-authoritative-plus-predicted on the owner; the
**interpolated** `SnapshotBuffer.RenderTick` on a remote proxy; a local counter offline. A proxy uses
the interpolated tick and not the newest received one, because the body on screen is
`SnapshotBuffer.InterpDelayTicks` behind — seeking to the newest tick would put the pose ahead of the
position it belongs to.

**A looping clip wraps; a one-shot clamps and holds.** Getting that backwards makes a late joiner see
a knocked-out body restart its fall every 0.6 s. `ClipTimeAnchor`, and `ClipTimeAnchorTests`.

## 3. Markers fire cosmetic events only

Footstep, net-close, land: each fires **locally on every client from the same replicated state**.
Nothing with a gameplay consequence — hit detection, capture resolution, damage — keys off an
animation marker. A marker may *align* a sound or a puff to a frame; it may not decide an outcome.

- **The footstep seam is the latch that already exists.** `AvatarVisual.ConsumeFootPlant()` →
  `SandboxAvatar` → `FootstepAudioDirector.TryTakeStep` → `ActorFx.Fire(..., ActorEvent.Step, ...)`.
  The clip layer changes only *when* the latch is set: `_gaitPhase` now advances at the clip's warped
  cadence (`ClipTimeWarp.CadenceHzFor`) rather than at the procedural one, so the plant fires on the
  clip's own stance entry. The director, the voice budget and the
  `feat/2026-08-13-remote-footsteps` consumer are untouched.
- **What sound plays is not ours.** The dispatch brief's §6A pairs footstep sound to the dominant
  splat material plus modifier layers, and that belongs to the ground/sound lane. The seam is
  `FootstepAudioDirector.TryTakeStep`; nothing here picks a sample.
- **Never hang a marker on blink or idle fidget.** Both are `GD.RandRange`-seeded per instance and
  are deliberately not parity-identical (`AvatarVisual`'s blink/fidget fields). A marker there makes
  a per-instance random an input to a system that must converge.
- **`AnimationPlayer` method tracks are not used yet.** The library carries none — authoring one is
  an `assets/**` change. The latch above is driven from the clip's own phase instead, which is the
  same structure; a real method track is a straight substitution when the asset lane adds one.

## 4. The crossfade table

One table, keyed **per destination** rather than per pair — a state declares its own abruptness once
and cannot be given two different answers by two different origins. `AnimationCrossfade`, and
`AnimationCrossfadeTests`.

| Entering | Seconds |
|---|---|
| Locomotion, Skid, Jump_Launch, Jump_Air, Jump_Land | **0.15** |
| Stagger, KnockOut | **0** |
| Hold_Empty, Hold_NetReady, Carry_Handle, Carry_Armful | **0.15** |
| Hold_NetSwing (the net-close) | **0** |
| *inside* the locomotion blend space (Idle↔Walk↔Run) | **0** — see below |

**Zero means zero, not "very short".** A blend is a lie about what the body did: easing into a
knock-out over 0.15 s renders a body falling over gracefully, and the whole point of the hit is that
it was not absorbed. Raising it is a design change to what a hit reads as; it belongs to `/direct`,
not here.

**Inside the locomotion blend space there is no blend at all, and that is a measurement.** A weighted
Walk↔Run blend was measured off the shipped `Greybox.glb` bytes at up to **99.5 mm** of
planted-foot skate at weight 0.25, against **0.09 mm** (Walk) and **1.09 mm** (Run) at the pure
endpoints — the exact defect MOVE-1 removed, reintroduced by a blend weight. Cause: the duty-factor
mismatch (Walk plants for 35.4% of its cycle, Run for 14.1%), so through the overlap one clip's foot
is planted while the other's is already swinging. The blend space is therefore
`BlendModeEnum.DiscreteCarry`: it switches clip at the boundary while **carrying the playback
position**, so the phase is continuous — and because ANIM-M2's leg cap forces both clips onto the
identical 0.6859 m foot sweep, the pose is continuous too.
(`ClipTimeWarpTests.InterpolatedWalkRunBlend_SkatesFarWorseThanEitherEndpoint`.)

## 5. Clips versus modifiers — who owns which channel

**Six nodes are keyed, and only six**: `ThighL`, `ThighR`, `ArmL`, `ArmR` in pitch; `Waist`, `Head`
in yaw. Rotation only — there is not one translation channel in the library.

**The leg's names changed on 2026-08-21 (ANIM-M2b).** Until then the greybox's THIGHS were called
`FootL`/`FootR` — a grandfathered harvest-contract name on a body that had no feet — and this table
said so. Talon's ruling, verbatim: *"I would like the thigh renamed, yes. I would like the foot
called FootR and FootL, this is fine."* So the chain is `ThighL → ShinL → FootL`, `Foot*` means a
foot, and a seventh channel exists. **The rename is greybox-only**; the creature rigs still speak the
old dialect — see `BLENDER-EXPORT.md`'s migration note.

| Channel | Owner under `--authored-clips` | Note |
|---|---|---|
| Hip pitch (`ThighL/R`) | the clip **proposes**, `LimbIk` **disposes** | The clip's angle goes into the same `SolveLeg` call the derived gait used, so `LimbIkTests.RigidGaitFootPositions_AreReproducedExactly` stays a test rather than a deleted one. |
| Knee (`ShinL/R`) | `LimbIk`, always | Never keyed. Keying a shin deletes the guarantee above. |
| **Ankle (`FootL/R`)** | **`LimbIk`, always** | **Never keyed, and not for symmetry with the knee — for a stronger reason.** The angle that holds a sole flat is `-(hip + knee)`, and the knee is whatever `LimbIk.Solve` produced *on this frame* out of the jump tuck and the landing absorb. A clip author cannot know that number, so a hand-keyed ankle is wrong by construction on every frame the knee is not straight. `LimbIk.LevelAnkle` computes it from the solve; `AvatarVisual.SolveLeg` writes it last; `AnkleLevelRad(bool left)` reads it back. Clamped to 45° dorsi / 60° plantar, past which the foot rides the leg — which is the right read for a knock-out. |
| Shoulder pitch (`ArmL/R`) | the clip, entirely | The gait counter-swing, the swing arc and the airborne tuck are all authored channels now. |
| Elbow (`ForearmL/R`) | `LimbIk`, always | Same reason as the knee. |
| Waist **yaw** | the clip | Capped at 3.25° by ANIM-M2's facet-slip budget. |
| Waist **pitch** | the runtime, always | `AvatarVisual` writes `_waist.Rotation with { X = … }`, preserving Y. **Two writers on two components, no overlap** — and this only works because the library authors no trunk pitch. Do not add one without reading ANIM-M2 §8: that channel already exceeds the seam budget at full lean (12.0°, 47 mm of daylight). |
| Head yaw | the clip | Nothing in the procedural layer has ever written `Head.Rotation`. |
| Arm/hand **position** offsets | the runtime | Carry, aim and `SwingHandOffset` — the last of which also places the held net and is ANIM-1's ratified tuning. |

**Everything in ANIM-M0 §4.2 stays procedural** and is layered on top of the clip: the
squash-and-stretch spring on `Body` **only**, lean-as-acceleration, the crouch absorb, the face
channels, the blink and the idle fidget, the sprout sway, `FootLiftAt`'s swing-leg lift, and the
`AnimationSuspended` early return with its two parks.

**`FootLiftAt` stays procedural** (ANIM-M2's open item 3, answered): it is a translation and rule 1
of the library is rotation-only; it derives from `DutyFactor` and `StanceReachM`, which are outputs of
the very identity the time-warp preserves, so it stays in phase with a warped clip for free; and
`ThighL` is the node the solver owns.

**The ankle levels against the rig's rest plane, not against the terrain.** It asks no ground
question, on purpose: a per-foot raycast would be a second, differently-timed source of truth for a
body whose vertical is already owned by the derived bob and by reconciliation smoothing. That is what
makes the ankle pivot's height load-bearing — levelling carries the sole `ankleHeight × sin(hip)` off
the leg chain's own line, so the asset puts the pivot at **y = 0**, where the error is identically
zero at every angle and `FootContactGlobal` and the visible sole are the same point.
`GreyboxAssetContractTests.AuthoredAsset_HasAnAnkleOnTheGroundAndItsLegIsThighShinFoot` pins it.
Sloped ground is a later packet with a raycast budget attached.

**The incapacity branch has an equivalent hard branch in the tree.** `AnimationSuspended` returns
above every procedural writer; the clip layer is routed to `KnockOut` at zero crossfade *inside* that
branch, and both parks (the swing, the crouch) still happen — otherwise a knocked-out body holds a
cocked arm and sits 4 cm into the ground.

**`_aimAnchor` stays off the pose.** It will look like a bug. It is not: the aim origin is a gameplay
origin the server reads, and the pose is deliberately not parity-identical across peers (the idle
fidget is `GD.RandRange`-seeded per instance). Build the upper-body layer *around* it. Do not "fix"
it. (ANIM-1; `SandboxAvatar`'s own comment states the reason.)

## 6. The tree's shape, and two deliberate deviations

```
root : AnimationNodeBlendTree
  loco       AnimationNodeBlendSpace1D   Idle @0 · Walk @2.4207 · Run @9.1447   DISCRETE_CARRY
  warp       AnimationNodeTimeScale      ← loco        scale = ground speed ÷ clip nominal
  act_*      AnimationNodeAnimation      Skid, Jump_Launch, Jump_Air, Jump_Land, Stagger, KnockOut
  action     AnimationNodeTransition     7 inputs; xfade_time from the table in §4
  act_seek   AnimationNodeTimeSeek       ← action      the §2 seek
  hold_*     AnimationNodeAnimation      Hold_Empty, Hold_NetReady, Hold_NetSwing, Carry_Handle, Carry_Armful
  hold       AnimationNodeTransition     5 inputs
  hold_seek  AnimationNodeTimeSeek       ← hold        the swing's replicated-progress seek
  arms       AnimationNodeBlend2         in=act_seek  in2=hold_seek   FILTERED to ArmL, ArmR
  output     ← arms
```

1. **`AnimationNodeTransition`, not `AnimationNodeStateMachine`.**
   `AnimationNodeStateMachinePlayback` has no seek, and §2 requires one. `AnimationNodeTimeSeek` is
   the node that does that and it composes with a Transition. And the crossfade table is keyed per
   *destination*, which is exactly `xfade_time`'s shape; a state machine wants one
   `AnimationNodeStateMachineTransition` resource per ordered pair — 42 for seven states — each free
   to carry a different answer to a question that has one.
2. **`CallbackModeProcess = Manual`.** The tree is advanced explicitly from `AvatarVisual.Animate`,
   so "tree first, then modifiers" is a line of code instead of a hope about node ordering.

**The track filter is not an optimisation; it is what makes an override an override.** The `.glb`
carries two channels for `Carry_Armful`; Godot's importer pads **every** clip out to six tracks, one
per node animated anywhere in the file, with the missing ones constant at rest, and
`animation/remove_immutable_tracks` is already on and does not remove them (ANIM-M2 §7). An
unfiltered `Blend2` of an arm override over `Walk` at weight 1 therefore drives the **leg** tracks
with the override's padding and freezes the legs. The filtered paths are read out of a real animation
at build time rather than spelled out in code, so a re-export that moves `ArmL` in the hierarchy
keeps filtering the arm instead of silently filtering nothing.

**The swing's authored `Waist`/`Head` yaw is not layered** — one `Blend2`, arms only. Those channels
are 3.25° / 2.68°, inside a budget ANIM-M1 measured at 5 mm of daylight, and the swing's body twist
is already carried by a much larger procedural writer (`SwingYawRad` on `_pose`). A second filtered
`Blend2` for 3° of yaw is a node and a failure mode for nothing visible.

## 7. Animation LOD

`AvatarAnimationLod`. Tuned for **four players** — the supported session size.

| Tier | Distance | Behaviour |
|---|---|---|
| Full | < 18 m, or the local player at any distance | advances every rendered frame |
| HalfRate | 18–35 m | advances every other frame, **with the skipped frame's delta carried** |
| Frozen | > 35 m | holds its last evaluated pose; phase carried, never reset |

The carried delta is load-bearing: advancing a half-rate body by half the time would be a body
walking in slow motion — a different animation, not a cheaper one — and would break
`stride × cadence` for every remote body past 18 m. The local player is exempted by a flag rather
than by its distance being zero, because a third-person camera can legitimately be pushed several
metres back. Hysteresis is 2 m, for the reason `Gear` has hysteresis.

Cost: six padded tracks × (base + one filtered override) = 12 track evaluations per advance, so
720/s per full-rate body at 60 fps. Four players all in view and all full-rate is 2 880/s; six is
4 320/s.

## 8. The flag

`--authored-clips` → `AvatarClipFlag.AuthoredClips`, written in exactly one place in shipping code
(`Boot._Ready`), process-wide, default **OFF**.

**Off by default because it replaces something ratified**, not because of doubt. MOVE-1's and
SKID-1's gait was tuned against Talon's eyes over two packets and is asserted by `Run-SandboxTest`
and `Run-BodyLanguageTest`. Landing the structural change (the greybox row becoming
`Build.AuthoredRig`) and the behavioural one (clips driving the base pose) in one default puts two
unrelated risks in one change — the shape `AvatarVisual`'s own roster comment already refuses.

**It is also the instrument the migration exists to provide.** One build, both gaits, side by side in
one session. A default flip is a one-line change the day Talon says which.

**Presentation only, in both positions.** No simulation reads it, no snapshot carries it, and two
players in one session with different values still agree on every outcome. Every readout other
systems consume — `CadenceHz`, `GaitPhase`, `DutyFactor`, `StanceReachM`, `KneeBendRad`,
`ElbowBendRad`, `BodyTiltX`, `WaistPitchRad`, `CrouchDropM`, `BodyOffsetY`, `BodyYaw`, `CarryWeight`,
`SwingWeight`, `CarryBobOffset`, `SwingHandOffset` — keeps being written either way.

## 9. What is open, and must not be closed by inference

1. **Does the authored gait feel better than the procedural one?** Talon's, and the flag exists so he
   can answer it. Nothing here should be defaulted on before he does.
2. **A third gait clip at Jog (≈5.4 m/s).** The library has two clips 3.8× apart and the usable band
   is 0.45–8.64 m/s, so Jog — *the default gear* — is served by the Run clip warped down to
   0.30–0.72×. Arithmetically correct; it will read slow. The fix is an authored clip, which is
   `assets/**`.
3. **The two carries are not told apart.** `Carry_Handle` and `Carry_Armful` are both authored and
   both wired, but the carry *shape* is not a replicated fact — `SetCarrying` is a single bool. Every
   carry takes the armful until it is; the seam is one line in `DeriveHoldState`.
4. **`Stagger` has no cause.** The clip and the state exist and are reachable through
   `AvatarVisual.SetStagger`; no shipping mechanic raises one (`IncapacityState` is
   Active/KnockedOut/Frozen with a reserved 3). Declared rather than omitted so the state machine's
   rows are complete.
5. ~~**The trunk-pitch seam budget** — accept, ball, or cap (ANIM-M1 open question 2, ANIM-M2 §8).~~
   **RULED 2026-08-22 — see §9.1 below.**
6. **The ankle.** A foot segment added later invalidates every leg clip in the library
   (ANIM-M1 open question 1).

### 9.1 Ruled 2026-08-22 — cost over polish, until stated otherwise

Talon closed three items LEARN-1's report carried forward as open, all with the same standing
instruction: **pick whichever option is cheapest in animation, rendering or networked-communication
cost, not the most correct or most polished one.** Record here so a later packet doesn't re-open
any of these chasing a "nicer" answer without knowing that instruction was live when they closed.

- **The trunk-pitch seam budget (item 5, above) — leave the cap exactly where it is.** No widened
  cap, no ball-joint geometry at the waist or neck. The waist and neck stay flat-cut seams; the
  waist's daylight gap at extreme lean (47 mm at the shipped 12.0°) is accepted as a known
  characteristic of the technique, not something being fixed now. `Stagger` and `Jump_Land`
  continue to ship without the waist fold they were authored wanting. Zero cost, because nothing
  changes: no new geometry, no new self-check exemption, no new joint to animate or replicate.
- **No forward toe on the foot.** The foot stays the symmetric rounded pad it shipped as in
  ANIM-M2b. A toe would be a silhouette change requiring a per-part exemption from the "every part
  is a solid of revolution" self-check — not expensive to build, but ongoing cost to *own* (see the
  ANIM-M2b report's own accounting). Zero cost: no change from what already ships.
- **The ankle does not query sloped ground.** `LimbIk.LevelAnkle` keeps levelling against the rig's
  own rest plane; no per-foot raycast is added. A raycast would be a second, differently-timed
  vertical authority alongside the derived bob and reconciliation smoothing — real per-frame,
  per-player cost (and, if it ever needed to be authoritative rather than cosmetic, a networked
  one) for a problem that does not exist yet: today's walkable terrain is flat under the player.
  Revisit only when a level's walkable ground genuinely is not flat.

None of these required a code change — in each case the cheapest option was the behavior already
shipped. This section exists so that stays a documented decision, not an accident nobody chose.

---

## 10. The ride channel — the mounted rider

Added 2026-09-04 (MOVE-1), stating the channel **as built** rather than as designed. It came over
from `Sail`'s bike branch (`86b4190a`, `253220e5`) with the movement lab; `scripts/game/sandbox/
RidePose.cs` is the arithmetic and `AvatarVisual.SetRide` is the one door into it. Today the only
caller is the lab (`scripts/dev/playground/BikeLayer.cs`); nothing in the shipped level mounts.

**One entry point, and weight 0 is the exact no-op.**
`SetRide(weight, mode, steer01 = 0, leanRad = 0, crankPhase01 = -1)`. `weight` is
`BikeLayer.Blend` — 0 on foot, 1 fully riding. Every other parameter is optional and defaults to
its own no-op, so a body nothing has called `SetRide` on renders bit-for-bit the body it rendered
before this channel existed. A negative `crankPhase01` advances the local accumulator; a value in
0..1 seeks it, the same mid-join affordance `SetSwing`'s `progress01` is.

**Ride weight short-circuits the gait to Idle.** Any `weight > 0` forces `_gear = Gear.Idle`
before the idle read is taken, and `CadenceHz` is then multiplied by `1 − weight`. A rider takes
no steps, so the public cadence readout — which the footstep director and the self-tests both
read — must not report a sprinting body under a pedalling one. The multiply is deliberate: a
partial mount reports a partial cadence, and at weight 0 it is exactly the shipped `CadenceHz`.

**The cranks are a fixed-gear speedometer, not a clock.** Crank rate is derived from ground speed
and nothing else: `2π · 0.34 m · 2.75 = 5.875 m` of ground per crank revolution
(`RidePose.WheelRadiusM`, `RidePose.WheelRevsPerCrankRev`). There is no freewheel and no cadence
curve — at rest the cranks are still, at the ride cap they turn at 1.604 rev/s, and the ratio of
crank rates is exactly the ratio of speeds. `AvatarVisual.CrankHz` is the readout.

**Two poses, PEDAL and COAST, with a 0.3 s hysteresis and a 0.22 m/s coast threshold.**
`RidePose.Wants` resolves what the inputs are asking for — forward input held, and flat speed at
or below the wish plus `CoastOverWishMps` — and `RidePose.StepMode` will not take that answer
until it has been wanted continuously for `ModeHoldSec = 0.3 s`, in either direction. Below
`0.22 m/s` (`LocomotionProfile.IdleExitMps`, taken deliberately rather than invented so the
mount and the gait agree about "stopped") the cranks hold still even in PEDAL. PEDAL leans the
body 0.21 rad forward, COAST 0.105 rad; COAST settles the crank phase to level rather than
snapping it.

**Airborne keeps the pose at 40 % and freezes the cranks.** Off the floor the ride pose is scaled
by `RidePose.AirAmplitudeFraction = 0.40` — the rider stays recognisably on the bike rather than
reverting to an on-foot air pose — and `CrankHz` goes to 0, because the wheel is not on anything.
The ride weight itself is unchanged by leaving the ground; only the amplitude and the cranks move.

**The bank is crossfaded, never added twice.** The body's roll is `_turnRollRad · (1 − weight) +
_rideLeanRad · weight`: on foot it is the on-foot turn roll bit for bit, mounted it is the
machine's own lean from `BikeHandling`, and in between it is one bank rather than two summed.
`CrouchDropM` is likewise lerped to zero with ride weight — a rider's weight is on the saddle, so
a mounted landing absorb that dropped the hips would sink the body through the bike.

`tests/unit/RidePoseTests.cs` pins the arithmetic, and `--bike-selftest`'s five `ride_*` checks
(`ride_at_the_cap_is_not_sprint_gait`, `ride_crank_rate_is_the_cadence_rule_and_zero_at_rest`,
`ride_pedal_and_coast_are_two_poses`, `ride_airborne_freezes_the_cranks_and_keeps_the_weight`,
`ride_channel_parks_while_incapacitated`) pin it in the engine on a real body.
