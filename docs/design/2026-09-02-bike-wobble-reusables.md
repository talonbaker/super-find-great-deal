# What Wobble can lend the fake — BIKE-3C

**Date:** 2026-09-02
**Packet:** BIKE-3C (systems-design)
**Branch:** `docs/2026-09-02-bike-3c-wobble-reusables`
**Audited from code on both sides:** the Wobble repos at `C:\repos\Wobble Game Repos\`
(read-only reference, paths below are relative to that root) and this lab's bike at
`scripts/dev/playground/` @ `8bf0c2a9`.

**Spec only.** No code changes. Every verdict below respects the two seams this lab
enforces — **no second writer of `Velocity`** (only `BikeLayer` writes it, `BikeRig`
computes what), and **tuning flows through the single writer**
(`BikeLayer.SetTuning` / `MotorTuning.TryApply`). No row proposes a new seam; the one
row that needs an impulse (§2, accel texture) rides the impulse-request seam
`BikeHandling.cs` already documents as pending (`BikeLayer.RequestImpulse`,
`scripts/dev/playground/BikeHandling.cs:24-29`) and is costed against it.

---

## 0. The one-paragraph version

Wobble's bike feel does not live in a steering model — it lives in a **coupling**: the
player leans (never really steers), the full Whipple simulation turns lean into steering,
and every ounce of "this is a bicycle" comes from the fact that that coupling is
**unstable slow and self-stable fast**. None of that arithmetic is portable — it needs the
model the ask explicitly rules out — but three things are: the **speed–stability signature**
(wobble at walking pace, calm at speed) as a presentation-only ingredient; the **fixed-gear
cadence rule** (cranks rigidly coupled to wheel speed — one line, a direct gift to
BIKE-3B); and the **architecture lesson** the lab has already independently converged on
(honest core, feel applied from outside as pure functions over a small POD state — which is
also exactly what made Wobble's bike predictable over a network). Almost everything else is
either already present in the lab in a better-suited form, or is welded to Wobble's crash
loop, which this lab's no-lockout law rules out.

---

## 1. The honest map — how Wobble's bike movement actually works

All claims below are from code, with paths. The pitch/handoff docs were read for context
only; where they disagree with code, the code is cited.

### 1.1 The simulation core

- `wobble-bike-engine/src/Bicycle.Physics/Runtime/BicycleSimulator.cs` — the full
  nonlinear Whipple EOM integrated with RK4 at a fixed 4 ms (250 Hz), double precision,
  allocation-free (the cached-delegate comment at `:28-30` exists to keep a CI
  allocation gate green).
- State is `BicycleState.cs`: 8 generalized coordinates + 3 rates, a bit-blittable POD
  (`[StructLayout(Sequential)]`, "MemoryMarshal.AsBytes is safe" — `:11-12`).
- Input is **three torques**, nothing else: `ControlInputs.cs` — `SteerTorque`,
  `DriveTorque`, `RiderLeanTorque`. There is no "turn the bike" input; there are only
  torques the physics answers.

### 1.2 What produces steering feel: lean in, steering out

`wobble/unity/Assets/Bicycle/Runtime/Input/PedalInputSource.cs` (the shipped "perfect
feel" input model, per its own header `:8-9`) is the load-bearing file:

- A tap of F or J applies a **lean impulse of 200 N·m** and a steer nudge of only
  **12 N·m** (`_leanTapTorque` / `_steerTapTorque`, `:24-27`). The comment at `:25-26`
  says it outright: *"the gentle turn on a tap comes mostly from the lean impulse, not
  from cranking the bars."* Steering **emerges** from the Whipple lean→steer coupling
  (the caster/trail physics inside the EOM), it is not commanded.
- Direct hold-steer exists but is framed as **the wrong way to turn** — the comment at
  `:31-34`: *"direct-steering at speed will high-side and crash you (the 'wrong way to
  turn' lesson)."* Countersteer is therefore never *read* by any game code; it is a
  property of the model the player's hands discover.
- **The wobble that names the game is one term**: `_pedalLeanKick = 420` N·m per pedal
  stroke, kicked in the stroke's own direction, fading linearly to zero by
  `_pedalKickFadeSpeed = 9` m/s (`ComputePedalLeanKick`, `:501-518`). Its doc comment is
  the design in one line: *"wobble while you build speed, settle once you're fast …
  above it the real Whipple self-stability is left to hold the bike up on its own."*
- Below ~0.5 m/s a **"kickstand" PD controller on roll** (Kp 250 / Kd 150, faded out
  over a 0.3 m/s window — `ComputeKickstandLean`, `:426-454`) fakes the low-speed
  balance the model cannot provide (its comment: the Whipple self-stable window *"only
  opens above ~4.3 m/s"*). It is deliberately **switched off while braking** (`:309-315`)
  so committing to a stop means giving up the balance aid and tipping.
- A dormant steer-damping PD (`_steerDampKp/Kd = 0`, `:81-93`) is kept switched off with
  a comment that is itself a finding: *"the 'perfect feel' wobble lives in the low-speed
  lean→steer coupling that this would suppress."* (The 2026-06-14 mobile handoff records
  the session where turning it on killed the feel; the code comment is the surviving
  artifact.)

### 1.3 What produces speed

- Drive is **per-stroke bursts**, not a held throttle: a qualifying press fires
  800 N·m for 0.20 s (`_strokeDriveTorque` / `_driveStrokeDuration`, `:43-49`), gated by
  a 0.05 s activation delay so a quick tap is lean-only.
- Stroke drive is attenuated **linearly to zero by 9 m/s** (`_driveSpeedCutoff`,
  `:57-61`) — that sets the flat-ground top speed; the comment notes gravity ignores the
  cutoff *"so DOWNHILLS still build well past it and scream."*
- A **tempo multiplier** rewards clean alternation (`ComputeTempoMultiplier`,
  `:520-554`): same-side hammering ×0.25, mashing under 0.10 s ×0.6, a brisk clean
  cadence peaking at 0.30 s ×1.6, tapering to baseline by 0.70 s — *"forgiving, so it
  reads as rhythm, not a quick-time event."*
- Coasting decays via post-step rolling friction, `spinRate *= (1 − 0.2·dt)`
  (`BicycleSimulator.Step` `:136-149`; coefficient from
  `wobble-bike-engine/src/Bicycle.World/FeelPreset.cs:44-51`, "~18% speed loss per
  second of coasting").

### 1.4 How state is handled

- **There is no mount/dismount anywhere in Wobble.** The player *is* the bike; the game
  starts riding and ends fallen. The lab's mount grammar
  (`scripts/dev/playground/BikeLayer.cs`) has no counterpart to borrow from — worth
  saying plainly because the packet's ask names state handling as a candidate.
- The state machine Wobble does have is the **validity handoff**: the Whipple model is
  honest only inside its linearization, so at |roll| ≥ 45° (or NaN) the sim is frozen
  and a PhysX rigid body takes over for the fall
  (`wobble/unity/Assets/Bicycle/Runtime/BikeFallReset.cs:13-17, 66-75`;
  `View/BikeFallGravityHandoff.cs`; the engine-side invariant names in
  `wobble-bike-engine/src/Bicycle.Physics.PlaybackHarness/Invariants.cs:40-47`).
  Riding → fallen → (1.0 s delay) → reset. **The wipeout is a run-ending fail state** —
  the FELL screen and retry are the game loop.
- Game-feel guardrails are applied **outside the integrator, between steps**, by
  design: `BicycleSimulator.ClampSteer` (`:104-125`, one-sided so the bars can swing
  back), rolling friction post-step (`:142-146`, "adding it inside the EOM would shift
  the Meijaard eigenvalue benchmark"), one-shot impulses (`AddRollRateDelta`,
  `AddRearWheelSpinDelta`, `:71-91`). The honest core is never edited for feel.

### 1.5 What is simulation and what is presentation

The split is unusually clean, and the presentation list is longer than one would guess:

| Simulation (the Whipple state) | Presentation / game-side overlays (never in the sim) |
|---|---|
| roll, steer, yaw, pitch, wheel spin, position on an internal **flat plane** | **Terrain**: the sim never sees a hill. `Terrain/TerrainPhysicsAdapter.cs` raycasts the ground and rotates *gravity* into the bike frame — gravity direction is the only interface. |
| | **Airtime**: raycast-miss detection + a game-side parabola (`Terrain/BikeAirtimeController.cs` — `_launchDropThreshold`, restitution, bounce cap). |
| | **Curb/obstacle response**: a single sagittal contact impulse (`Terrain/SagittalImpact.cs`) feeding a wheelie **pendulum** (`Terrain/WheeliePitchDynamics.cs` — gravity-accelerating fall, lands at θ=0, rate killed) and a forward-endo crash barrier at 45°. |
| | **Steer stop + over-steer crash**: `SteerLimit.cs` clamps ±90° post-step and fires `OnOverSteer` above 2 m/s — slamming the bars at speed *crashes you*. |
| | **Pedal animation**: `View/BikePedalAnimator.cs`, three layers (below). |
| | **Rider**: procedural IK + passive pendulum legs (`View/BikeRider.cs`, `View/LegDangle.cs` — "nothing here knows about pedals, and nothing ever will"). |
| | **Crash**: PhysX ragdoll handoff, momentum conserved (`WipeoutTumble.cs` — forward momentum "is NOT deleted"; a 0.5 rad/s barrel-roll "garnish" explicitly labelled not physical). |

### 1.6 The network story (wobble-bike-multiplayer)

Server-authoritative at 250 Hz, 30 Hz snapshots, client prediction + reconciliation —
the same shape as this lab's `AvatarMotor`/`MoveState` netcode. What made it workable is
a property, not a library: **the whole bike step is a deterministic pure function over an
88-byte POD state**, so the client replays buffered inputs from any server state.
`src/Bicycle.Client/PredictionBuffer.cs` (128-entry ring, ~512 ms at 250 Hz),
`ReconciliationEngine.cs` (thresholds 1 mm position / 1 mrad angle-and-rate),
`InputBatcher.cs`. Determinism is per-machine bit-exact and CI-gated
(`wobble-bike-engine/CLAUDE.md`, locked decisions; `DeterminismTests.cs`).

---

## 2. The reusables table

Verdicts: **REUSE AS-IS** (portable pure code) / **REUSE IN SPIRIT** (the idea,
re-derived for this lab's seams) / **LEAVE** (with why). Landing sites name file +
function. "Net story" answers: deterministic pure function in the simulation, or
presentation-only (client-side, never on the wire)?

| # | Wobble ingredient (code) | Verdict | Landing site in this lab | Network story |
|---|---|---|---|---|
| 1 | Whipple EOM + RK4 core (`BicycleSimulator.cs`, `Generated/WhippleEOM.cs`) | **LEAVE** — the ask rules it out; needs double precision at 250 Hz against a 60 Hz `MoveIntent` tick, and its failure mode is §4.1's NaN. | — | — |
| 2 | **Speed–stability signature**: pedal-lean kick fading to zero by ride speed (`PedalInputSource.ComputePedalLeanKick:501-518`) + kickstand fade shape (`ComputeKickstandLean:426-454`) | **REUSE IN SPIRIT** — a low-speed lean wobble, presentation-only: a small oscillating roll term at walking pace that fades to zero by the ride jog, layered under `LeanSteadyDeg`'s corner lean. The lab's mounted body is currently *rock-steady at 1 m/s*, which is the single most un-bikelike thing about it (§3, gap 1). | `scripts/dev/playground/BikeHandling.cs` — a new pure `WobbleDeg(speedMps, phase, in h)` beside `LeanSteadyDeg`; two rows in `BikeHandlingTuning.cs` (amplitude deg, fade speed). Feeds the **visual roll only** — the same channel the lean already draws on — never `TurnMultiplier`, never tuning, never velocity. | Presentation-only. Pure function of replicated speed + local clock; each client draws its own; nothing on the wire. |
| 3 | **Fixed-gear cadence rule** (`BikePedalAnimator.ComputeCrankDelta:244-248` + `_wheelRevsPerCrankRev = 2.75`, `:56-59`) — cranks rigidly coupled to wheel speed; they slow as the bike slows and stop when it stops; "the pedal spin IS the momentum readout" | **REUSE AS-IS** — one line of portable arithmetic: `crankRate = speed / (wheelRadius × gearRatio)`. This is the cadence rule BIKE-3B's animation states can cite: pedal cycle rate proportional to ground speed, with a **coast threshold** below which legs hold still and the wheels alone roll (Wobble's cranks are fixed-gear so they never coast; a freewheel bike coasts — 3B picks the threshold). | BIKE-3B's `docs/design/2026-09-02-bike-rider-animation-states.md` (rule cited there); implementation eventually in the `AvatarVisual` ride overlay, driven by horizontal speed and `BikeLayer.Blend`. | Presentation-only; derived from replicated velocity, so every client's cranks agree to within interpolation error. Nothing on the wire. |
| 4 | **Net-zero overlay principle** (`BikePedalAnimator.StompOffset:250-262`): the per-press jab is `sin(π·u)` — rises, peaks, returns to *exactly zero*, so the overlay "never desyncs the true crank phase"; the base phase always advances | **REUSE IN SPIRIT** — the principle, for any pose overlay 3B designs (stomp on acceleration, drift lean-in, landing squash): overlays return to zero; the base channel (crank phase, lean, gait) is never written by an overlay. This is the animation-side twin of the lab's single-writer law. | BIKE-3B's animation-state doc, as a stated rule for every overlay it defines. | Presentation-only. |
| 5 | **Tempo/cadence drive reward** (`ComputeTempoMultiplier:520-554`) and per-stroke burst drive (`:43-61`) | **LEAVE** — welded to the two-button input grammar. This lab's speed is a held stick through `AvatarMotor`; press-rhythm-as-throttle would be a new input grammar Talon has not asked for, and its natural implementation is a stream of impulses (a second velocity writer, or a drumbeat through the pending `RequestImpulse` seam — cost: one seam, plus prediction has to replay impulse timing). The *reward-shape* (penalise mash, reward rhythm, forgive slow) is worth remembering if a pump/pedal verb is ever designed. | — | — |
| 6 | **Speed-attenuated drive cutoff** (`_driveSpeedCutoff = 9.0`, `:57-61`): player power fades to zero at top speed, gravity is exempt | **REUSE IN SPIRIT** — already half-present: the lab's slope bonus (`BikeRig.SlopeBonusStep`) is exactly "gravity is exempt from the cap" (wish bonus above `MoveSpeed`, capped by a chosen knob). The transferable corroboration: Wobble's flat-ground pedal ceiling ~9 m/s against its ~4.3 m/s stability crossover matches the lab's 9.4 m/s ride cap against a 6.1 m/s foot sprint. The numbers agree that **~9–9.5 m/s reads as "bike fast"** — keep the cap there when tuning. | No change — a tuning note against `BikeTuning.RideSpeedMul` (`scripts/dev/playground/BikeTuning.cs:37`) and `SlopeBonusMaxMps`. | Already inside the tuning seam; deterministic. |
| 7 | **Exponential coast** (rolling friction `spinRate *= (1 − k·dt)`, `BicycleSimulator.Step:142-149`, k=0.2 from `FeelPreset.cs:44-51`) vs the lab's linear `RideDeceleration = 2.5 m/s²` | **LEAVE** — the shape difference (proportional-to-speed vs constant) is real but small inside a 9.4 m/s envelope, and reshaping deceleration is an `AvatarMotor` change the tuning seam cannot express (`Deceleration` is one linear row). Not worth a motor edit; revisit only if coasting ever *feels* wrong in a headed run. | — | — |
| 8 | **Steer stop + over-steer crash** (`SteerLimit.cs:50-84`) — the price of speed is a turn you cannot make | **LEAVE** — the crash half contradicts the no-lockout law directly, and the honest half is **already implemented better for this lab** by the turn curve: `BikeHandling.TurnMultiplier` widens the turn at speed, and the drift is the sanctioned way to buy the corner back. Wobble punishes the over-ask with a wipeout; this lab prices it in steering authority. Same design goal, lab's answer is the compliant one. | — | — |
| 9 | **Sagittal impact + wheelie pendulum** (`Terrain/SagittalImpact.cs`, `WheeliePitchDynamics.cs`) — curb response emergent from one contact impulse; pitch falls under gravity, accelerating as it drops | **LEAVE** (for this wave) — its consumers are Wobble's crash loop (endo barrier → wipeout) and this lab has, by design, no crash: a curb is `BikeLayer.TryStepUp` and a hard case is a stop, not a fall. If a curb hit is ever wanted to *read* as an event, the pendulum (a 61-line pure `Step`) is the proven shape and would land as a presentation pitch overlay — but nothing consumes it today, and building it now is foundation-before-polish inverted. | — | — |
| 10 | **Crash presentation chain** (`WipeoutTumble.cs`, `View/BikeFallGravityHandoff.cs`, `View/CrashBodyFactory.cs`) — momentum-conserving ragdoll handoff | **LEAVE** — "no recovery lockouts anywhere" rules out the wipeout as a state; the lab's stumble (steer-scaled, sprint-off, jump kept — `BikeLayer.cs:317-326`) is the compliant analogue and already exists. The one conserved idea — a failure carries your real momentum rather than deleting it — is *already* the stumble's design (`StumbleClamp` clamps, never zeroes). | — | — |
| 11 | **Low-speed balance assist (kickstand PD)** (`ComputeKickstandLean:426-454`) | **LEAVE** — the fake has no balance to lose; `AvatarMotor` cannot fall over. The fade *shape* (full below a threshold, linear window to zero) is the same shape the lab already uses everywhere (`TurnMultiplier`, grip). Nothing to add. | — | — |
| 12 | **Deterministic pure-step + POD state as the net contract** (`PredictionBuffer.cs`, `ReconciliationEngine.cs`; `BicycleState`'s blittable layout) | **REUSE IN SPIRIT** — as the standing rule for every future handling ingredient: keep it a pure function of (state, intent, tuning) over value types, exactly as `BikeHandling.DriftStep` already is. Wobble is the existence proof that a bike shaped this way predicts and reconciles cleanly at 250 Hz; the lab's `DriftState` (a `readonly record struct`) is wire-ready the day the drift becomes server-authoritative. The concrete check for any REUSE row in this table: *could `DriftStep` replay it from a snapshot?* Rows 2–4 pass trivially (presentation); row 6 is already tuning. | Already the lab's shape — `BikeHandling.cs`/`BikeRig.cs` stay pure; this row is the reason they must stay so. | This row **is** the network story. |
| 13 | **Proven feel constants** — gear ratio 2.75 (`BikePedalAnimator:56-59`); stomp 0.25 s after a 0.05 s beat of hesitation (`:67-75`); mash threshold 0.10–0.12 s, cadence sweet spot 0.30 s (`:77-80`, `PedalInputSource:111-124`); stability crossover ~4.3 m/s (comment, `PedalInputSource:429`); wobble fade 9 m/s = top speed (`:107-109`) | **REUSE AS-IS** (as numbers, where a row consumes them): 2.75 and the stomp timings go to BIKE-3B's cadence/pose rows; ~4 m/s is the anchor for row 2's wobble-fade knob (wobble should die out between walk and ride jog — Wobble faded it at *top* speed because pedaling was the destabiliser; the lab's wobble is idle texture, so fade it earlier); 0.30 s is the beat a pedal-stomp overlay should sit near. Torque values (200/420/800 N·m) do **not** transfer — they are N·m against a real inertia in a different model (§4.5). | `BikeHandlingTuning.cs` (row 2's knobs), BIKE-3B's docs (cadence/pose). | Tuning values; deterministic where consumed by tuning, presentation where consumed by animation. |

No row above proposes a second velocity writer or a tuning bypass. Row 5 is the only
candidate whose natural shape *would* have needed one, and it is LEFT with that named as
part of the reason.

---

## 3. The gap read — what the fake most lacks that Wobble had

Ranked, in feel terms, so the next implementation packet can be cut from the top. Each
names the Wobble ingredient (or absence) that motivates it.

1. **Speed–stability coupling — the bike is dead calm at walking pace.** Wobble's whole
   identity is that low speed is alive (pedal-lean kicks through the lean→steer coupling,
   `ComputePedalLeanKick`) and high speed is serene (Whipple self-stability). The lab has
   the top half — `LeanSteadyDeg` gives a committed, banked corner at speed — and
   *nothing* at the bottom: lean is `atan(v·ω/g)`-proportional, so a slow bike is a
   statue, indistinguishable from standing. Table row 2 is the cut: a presentation-only
   low-speed wobble that fades by the ride jog. Cheapest possible "this is a bike"
   signal, zero mechanical or network cost.
2. **The pedals as a momentum gauge.** In Wobble you *see* your speed in the cranks
   (fixed-gear coupling, `ComputeCrankDelta`); the lab's mounted body runs the on-foot
   leg cycle, which is precisely Talon's ISSUE-3 complaint ("running with the bike
   clipped to their front"). Table rows 3–4 are the cut, and they are BIKE-3B's to land —
   this packet hands 3B the rule (crank rate = speed / (wheelRadius × 2.75), coast
   threshold, net-zero overlays) so its states can cite it.
3. **Acceleration has no texture.** Wobble's speed is earned in strokes on a beat
   (burst drive + tempo reward); the lab's is a held stick reaching a wish. The
   *compliant* cut is presentation-first: a pedal-stomp pose overlay on acceleration
   (row 4's principle, 3B's state machine) — the beat without the impulses. A mechanical
   version (drive pulses) is deliberately NOT recommended: it needs the impulse seam per
   pulse and a new input grammar (table row 5).
4. **Coast/roll-out character.** Proportional-vs-linear decay (table row 7). Real but
   minor at this speed envelope; behind everything above.
5. **A price for over-asking at speed.** Wobble crashes you for cranking the bars at
   speed (`SteerLimit.OnOverSteer`); the lab's answer must never be a lockout, and its
   turn curve + drift-grip already price the corner. If a future packet wants more here,
   the channel is *steering feel* (grip, lean overshoot), never control loss. Listed
   last because the law bounds it, not because it is absent.

---

## 4. Surprises worth a warning — so the implementer doesn't rediscover them

1. **The Whipple model NaNs *before* a dynamic fall, and the valid envelope is
   non-monotonic.** The tier-1 fall scenario says it in its own comment: *"the Whipple
   model integrator NaNs out at moderate initial rolls like 0.85 rad … but survives long
   enough to fire BikeHasFallen when started at ~π/2+0.05 rad"*
   (`wobble-bike-engine/tests/scenarios/fall-event-flat-overlean.json`, `_comment`;
   `rollPerturbation: 1.62`). Dynamic falls are "intrinsically unrepresentable"; every
   fall test uses synthetic-prefallen states. **Consequence for anyone reading Wobble's
   suites:** they assert *detection wiring* and *regression baselines*
   (`BikeFeelRegressionTests.cs` header: "golden-master tests that lock the exact
   numerical dynamic behavior"), never "the bike stays upright" — do not mine them for
   feel claims they do not make.
2. **The terrain is not in the simulation.** The sim rides an internal flat plane
   forever; hills exist only as a rotated gravity vector pushed in per step
   (`BicycleSimulator.SetGravity:60-69`, `TerrainPhysicsAdapter`). Reading Wobble code
   expecting the sim to know about slopes, curbs or air will mislead — every one of
   those is a game-side overlay (§1.5). The lab made the same discovery independently
   (`BikeTuning.RideSlopeGain`'s measured note: the motor keeps velocity horizontal on
   slopes), which is corroboration, not coincidence.
3. **Sign conventions bite twice.** The raw EOM's torque signs are *negated at the
   Dynamics boundary* so callers see intuitive signs (`ControlInputs.cs:9-15`, "gotchas
   #4 and #9"), and forward motion is a **negative** `RearWheelSpinRate`
   (`BikePedalAnimator._spinDirectionSign` exists solely to cancel it). Any number or
   formula lifted out of Wobble needs its sign convention checked at the boundary it
   crossed. The lab's own defence is the written sign convention in
   `BikeHandling.LeanSteadyDeg` — keep writing them down.
4. **The perfect feel died twice from well-intentioned damping.** The dormant
   steer-damp PD in `PedalInputSource.cs:88-91` is kept at zero with a comment that it
   would suppress the coupling the wobble lives in; the 2026-06-14 mobile handoff
   records the session that proved it (steer-damp + strong hold-steer "killed the
   wobble," reverted). **The portable lesson:** when feel lives in a coupling, damping
   the coupling deletes the feel. In this lab that reads: tune `LeanRatePerSec` and the
   turn curve so the settle into a corner stays *visible* — a lean lag fast enough to
   look instant is a lean nobody feels, which is the same failure at a different address.
5. **Wobble's tuned magnitudes are torques against a real inertia — they do not
   transfer.** 200/420/800 N·m mean nothing to a motor that consumes m/s and lerp
   rates. What transfers is dimensionless or in m/s: ratios (2.75), speeds (~9 m/s cap,
   ~4.3 m/s crossover), and times (0.05/0.20/0.25/0.30 s). Table row 13 lists exactly
   these and nothing else.
6. **Wobble solved multiplayer by being deterministic, not by being simple.** A full
   nonlinear physics model reconciled at 1 mm / 1 mrad thresholds
   (`ReconciliationEngine.cs:17-27`) because the step was a pure function over a POD.
   The lab's fake is *simpler* than Wobble's sim; if a handling ingredient ever feels
   hard to predict/reconcile, the ingredient is shaped wrong (hidden state, node reads),
   not the netcode.

---

## 5. Answer to the ask, in one paragraph

Adapt, don't port, and mostly *confirm*: the lab's current fake — tuning rewrite through
the single writer, pure-function handling model, lean derived from the turn — is already
the correct skeleton, and Wobble's code independently validates every one of those
choices (feel outside the honest core, POD state, presentation split). What Wobble
actually lends is small and specific: the low-speed wobble as presentation (gap 1), the
fixed-gear cadence rule and net-zero-overlay principle for BIKE-3B (gap 2), a handful of
speeds and times with proven feel (row 13), and two warnings (NaN class, coupling-damping
class) that would each have cost a session to rediscover. Everything welded to Wobble's
crash loop stays behind, because a reusable that cannot survive the no-lockout law is not
reusable here — and most of Wobble's danger-side feel is welded to exactly that loop.
