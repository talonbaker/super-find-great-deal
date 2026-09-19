# The movement verb state machine — MOVE-5a

**Date:** 2026-08-27
**Packet:** MOVE-5a (systems-design)
**Branch:** `feat/2026-08-27-move-5-verbs`
**Implements:** MOVE-5b onward (all code)
**Builds on:** `docs/design/2026-08-26-airborne-control-and-jump-shape.md` (MOVE-3a) and
`docs/design/2026-08-26-movement-tuning-surface.md` (MOVE-4a), both of which stay
authoritative for everything they decided. **Nothing in MOVE-4's thirty knobs is retuned
here.** Two of MOVE-3a's clauses acquire one amendment each; §9 states them, with the
arithmetic that forces them.

**Spec only.** No code, no scene, no test. Every value below is a knob (§11).

---

## 0. The one-paragraph version

One button already carries every verb. `MoveIntent.Jump` (edge) fires the jump, exactly as it
does today and with exactly today's zero latency; `MoveIntent.JumpHeld` (level) already rides the
wire; and because a held button arms no jump buffer (MOVE-3a §6.2, shipped), a player who is
**grounded with the button still down after a landing** is a state the motor can already
observe and does nothing with. That state is the whole wave: hold past a 0.20 s window and the
body crouches — into a **slide** if it is moving above sprint-entry speed, into a **tuck** if it
is not — and a slide that runs itself out under forward input **settles** into a latched **duck
walk**. Release pops you up in one tick, always, from anything. A **chain jump** rewards
consecutive hops with a raised *wish speed* rather than a velocity hack, so it survives the
airborne ceiling; it decays one level per half second instead of resetting, and it is read by
partners through a silhouette, never a UI element. The **double jump** ships off, with a
traditional variant and a proposed alternative — **the Kick**, which converts fall speed into
forward speed and reaches 94% of a traditional double jump's distance at 39% of its altitude,
which is the entire argument against reading floaty. The **skid–slide momentum share** turns out
to be one line, because both states are already nothing but `Velocity`; what is hard is not the
network, it is the *entry*, and that is the one thing one button cannot buy without latency.
**Total wire cost: three bytes on the snapshot, zero on the input entry, one protocol bump.**

---

## 1. Arithmetic and feel, separated once

Following MOVE-3a's practice, which MOVE-4e vindicated by falsifying a premise that had *not*
been labelled:

- **ARITHMETIC** — derived on the discrete model MOVE-4a §2.1 fixes (`TickDelta = 1/60`,
  semi-implicit Euler, gravity chosen from the velocity at the *start* of the tick). Every
  figure so marked is shown, not asserted, and reproduces the measured baselines it starts from:
  held sprint apex **1.534 m** / airtime **0.717 s**, jog tap apex **0.467 m** / airtime
  **0.317 s**, a tap landing at **5.27 m/s**, a held sprint jump landing at **8.64 m/s**
  (MOVE-4e, measured in engine).
- **FEEL — UNVERIFIED, reasoned not measured.** Every claim about how a verb *reads* is
  unverified until Talon plays it. That is the entire reason each of the 22 values in §11 is a
  knob rather than a decision.

Where a figure uses MOVE-3a's continuous convention it says so. This document's own baseline
for a held sprint jump's horizontal reach is the **discrete** `8.64 × 0.7167 = 6.192 m`, not
MOVE-3a §3.3's continuous 6.13 m; the two are the same jump measured on two clocks, and mixing
them is what makes a chain-bonus percentage wrong.

---

## 2. The two settled rulings, as they land in the machine

Restated only to show where they bite. **Neither is re-opened.**

**Ruling 1 — the button conflict resolves by context.** Grounded + holding = a crouch verb;
holding through a jump = height. Expressed structurally rather than as a branch:

> **Law V0. Every crouch verb requires `Grounded`. Leaving the floor forces `Verb = Normal` on
> the tick `Grounded` goes false.**

A body cannot be grounded and airborne at once, so V0 is the ruling, made unbreakable by a data
shape rather than by a check somebody must remember. It also kills, at no cost, the entire
family of bugs in which a duck-walking body walks off a ledge and stays ducked.

**Ruling 2 — the touchdown case is a knob, not a decision.** `TouchdownSlideImmediate` (knob 32),
both behaviours built, defaulted and argued in §4.3.

**The consequence of ruling 1 that this spec must state out loud.** A grounded press *always*
fires a jump — coyote is refilled on every grounded tick, so a grounded press is never denied.
Therefore **the ground hold window is only ever reachable after a landing**: the fluid input
"sprint, hold the button, drop into a slide" is really "sprint, hold the button, *jump*, land,
slide." That is not a defect of this spec; it is the price of one button with zero latency, and
the only alternatives are a deferred launch (rejected in §4.1 — it costs frames, which is
forbidden) or a second binding (Open question O1, §13). It is named here rather than discovered
in the lab.

---

## 3. The state machine

### 3.1 The three axes, and which of them are real

| Axis | Values | Where it lives | Real sim state? |
|---|---|---|---|
| **Ground/air** | grounded, airborne | `MoveState.Grounded` (shipped) | yes |
| **Skid** | skidding, not | `MoveState.SkidRemaining` (shipped) | yes |
| **Verb** | Normal, Tuck, Slide, DuckWalk | `MoveState.Verb` (**new**, 2 bits) | yes |
| **Gear** | Idle, Walk, Jog, Sprint | derived from speed by `LocomotionProfile.GearFor` | **no** — presentation |
| **Control lock** | the `ControlLockedBy` union | `MoveState` (shipped) | yes, and it dominates |

The packet's list names "standing, walking, running/sprinting" as states. They are **gears**, not
sim states: `AvatarMotor.Step` has no branch on them, `LocomotionProfile` derives them from
ground speed with hysteresis for the pose layer alone, and this spec adds no branch on them
either. They appear in the table below as the three readings of `Verb = Normal, Grounded`, and
that is the honest shape. Saying so is load-bearing: a verb that branched on gear would put a
presentation-layer enum into the reconciliation path.

### 3.2 The six states, each with entry, exit and behaviour

`ARITHMETIC` unless marked. All thresholds' inclusive/exclusive sense is stated (MECHANICS §1).

---

**A — GROUNDED-NORMAL** (`Verb = Normal`, `Grounded`, `SkidRemaining = 0`). *Covers the Standing,
Walking and Running gears.*

- **Entry:** the default. From airborne on the tick `Grounded` returns true; from any verb on
  release (`¬JumpHeld`); from a jump firing; from a control lock clearing.
- **Exit:** a crouch verb (§4.2), a skid (`ShouldEnterSkid`, shipped), a jump, leaving the floor,
  a control lock, or entering water.
- **Behaviour:** entirely unchanged from today, **except** for one added term: while
  `ChainDepth > 0` the ground wish speed gains `ChainDepth × ChainBonusMps` (§6). At the shipped
  default `ChainBonusMps = 0.00` that term is the exact no-op and this state is bit-identical to
  MOVE-4's.
- **Simultaneous entry/exit:** resolved by §3.4's order. A jump firing on the same tick a verb
  would enter → **the jump wins** and the verb does not enter.

---

**B — SKID** (`Verb = Normal`, `Grounded`, `SkidRemaining > 0`). *Shipped, SKID-1. Unchanged
except for two clauses.*

- **Entry:** `AvatarMotor.ShouldEnterSkid` — unchanged — **plus one new refusal:
  `Verb == Normal`.** A skid cannot start inside a crouch verb.
- **Exit:** the four shipped exits (clock, `SkidExitSpeedMps`, stick released, floor/lock/ballistic)
  **plus one: entering `Slide` cancels the skid** (`SkidRemaining = 0`), which is the whole of §8.
- **Behaviour:** unchanged. Steering suppressed, velocity pulled to zero at `SkidDeceleration`,
  facing follows travel.
- **Simultaneous entry/exit:** skid entry and slide entry on the same tick → **the slide wins**
  and the skid is suppressed (§3.4 case 6). Deliberate player intent beats an automatic
  consequence, and this is exactly the case that carries the momentum share.

---

**C — AIRBORNE** (`Verb = Normal`, `¬Grounded`). *Shipped. One addition.*

- **Entry:** `Grounded` goes false, by a jump, a ledge, or a slide running off an edge. **V0
  forces `Verb = Normal` and `VerbClockTicks = 0` on this tick**, whatever the verb was.
- **Exit:** `Grounded` returns true.
- **Behaviour:** unchanged — `GravityFor`'s three-way branch, the air fractions, the airborne
  wish ceiling — **plus** the air jump (§7) if `AirJumpMode > 0`, and **plus** §9's momentum
  grant while `ChainDepth > 0 ∨ AirJumpsUsed > 0`. Both are exact no-ops at the shipped defaults.
- **Simultaneous entry/exit:** a body whose `Grounded` chatters on a slope (a real Godot
  `MoveAndSlide` behaviour) forces `Verb = Normal` on every airborne tick. **The failure mode is
  therefore a verb that will not start, never a verb that strobes** — §3.5.

---

**D — CROUCH-TUCK** (`Verb = Tuck`, `Grounded`). *New. The stationary crouch; also the
below-speed outcome of every crouch entry.*

- **Entry:** §4.2's crouch trigger fires with ground speed **below** `SlideEnterSpeedMps`
  (exclusive: at exactly the threshold you get a Slide, not a Tuck). Also: a Slide that ends by
  its speed or duration exit while the settle condition (§5.2) is **not** met.
- **Exit:** `¬JumpHeld` → Normal, **on the next tick, unconditionally** (the Tuck is a *held*
  state); a `Jump` press edge → Normal + a jump; leaving the floor; a control lock; entering
  water.
- **Behaviour:** the wish speed is capped at `DuckWalkSpeedFraction × MoveSpeed` (2.43 m/s at the
  default). **Everything else is unchanged** — full `Acceleration`, full `Deceleration`, full
  `TurnLerp`, full `TurnAcceleration`. A player pushing no stick simply does not move, which is
  the brief's "holding while stationary is just a stationary crouch-tuck". *There is no
  responsiveness cost of any kind in this state; that is the point.*
- **Simultaneous entry/exit:** entry and release on the same tick → **release wins**, the state
  is never entered. The button is the latch and an up button can never hold a held state.

---

**E — CROUCH-SLIDE** (`Verb = Slide`, `Grounded`). *New. §4 is its full spec.*

- **Entry:** §4.2's crouch trigger fires with ground speed **at or above** `SlideEnterSpeedMps`
  (inclusive `>=`, matching `ShouldEnterSkid`'s convention exactly).
- **Exit:** `¬JumpHeld` → Normal (a *pop-up*, per the brief, speed preserved); ground speed
  **at or below** `SlideExitSpeedMps` and `VerbClockTicks ≥ SlideMinSec` → settle (§5.2);
  `VerbClockTicks ≥ SlideMaxSec` → settle; leaving the floor → Normal (§4.6); control lock;
  water.
- **Behaviour:** §4.4 — speed decays at `SlideDeceleration` along the current heading, the
  heading rotates toward the stick at `SlideTurnRateDeg`, and the stick contributes **no**
  acceleration and **no** braking. Facing follows travel, as the skid's does.
- **Simultaneous entry/exit:** speed-exit and release on the same tick → **release wins** → Normal
  (not DuckWalk). The release is the player's explicit "pop up" and an implicit settle must never
  override an explicit input.

---

**F — DUCK WALK** (`Verb = DuckWalk`, `Grounded`). *New. The one **latched** verb.*

- **Entry:** one edge only — a Slide that settles (§5.2). **There is no other entry.** That is
  faithful to the brief, which defines the duck walk in exactly one way ("settles into"), and it
  is what makes §3.5's cycle analysis structural rather than numeric.
- **Exit:** a `Jump` press edge → Normal **and a jump fires the same tick**; leaving the floor;
  a control lock; water. **`¬JumpHeld` does NOT exit** — the settle *is* the latch, and the brief
  requires the duck walk to be holdable indefinitely without a held button.
- **Behaviour:** identical to the Tuck's. Wish capped at `DuckWalkSpeedFraction × MoveSpeed`,
  everything else full.
- **Simultaneous entry/exit:** settle and press-edge on the same tick is impossible — a settle
  requires the previous tick to have been a Slide, and a press edge on that tick would have
  exited the Slide to Normal first (§3.4 order, rule 3 before rule 4).

---

**Control-lock states** (`ControlLocked`, `Incapacity ≠ Active`, `ImpulseRagdoll`, and
`ballistic`) are unchanged and **dominate everything**: they force `Verb = Normal`,
`VerbClockTicks = 0`, `ChainDepth = 0`, `ChainTimerTicks = 0`, `AirJumpsUsed = 0`. One line in
§3.4 rule 1, and it means no new state can survive a shove, a freeze, a sputter-out or a blast.

### 3.3 The transition table — one row per transition

`H` = `JumpHeld` true. `E` = a `Jump` press edge this tick. `v` = ground speed, m/s.
`W` = `VerbClockTicks ≥ JumpHoldWindowSec × 60`. Rows are evaluated in §3.4's order; the first
matching row in each rule wins.

| # | From | To | Trigger | Notes |
|---|---|---|---|---|
| T1 | any | Normal | control lock, incapacity, ragdoll or ballistic | Dominates. All clocks and counters zeroed. |
| T2 | any verb | Normal | `¬Grounded` | Law V0. Velocity untouched. |
| T3 | any verb | Normal | `Water ≠ Dry` | §4.7. Prevents a tuck in the shallows. |
| T4 | Normal, grounded | Airborne | `E` and `StepJump` fires | Unchanged. Chain updated (§6.3). |
| T5 | Tuck / DuckWalk | Airborne | `E` and `StepJump` fires | The press both stands you up and jumps. §5.4. |
| T6 | Slide | Airborne | `E` and `StepJump` fires | Cannot occur: `E` requires a release first, and the release already fired T9. Proof, not branch. |
| T7 | Airborne | Normal, grounded | `Grounded` returns true | `ChainTimerTicks` reset to grace (§6.4). |
| T8 | Airborne | Slide / Tuck | `Grounded` returns true ∧ `H` ∧ `TouchdownSlideImmediate = 1` | Slide if `v ≥ SlideEnterSpeedMps`, else Tuck. §4.3. |
| T9 | Slide / Tuck | Normal | `¬H` | The pop-up. One tick. Speed preserved. |
| T10 | Normal, grounded | Slide | `H` ∧ `W` ∧ `v ≥ SlideEnterSpeedMps` | The ground hold window. §4.2. |
| T11 | Normal, grounded | Tuck | `H` ∧ `W` ∧ `v < SlideEnterSpeedMps` | Same trigger, below the threshold. The boundary is never a dropped input. |
| T12 | Slide | DuckWalk | (`v ≤ SlideExitSpeedMps` ∧ clock ≥ `SlideMinSec`) ∨ clock ≥ `SlideMaxSec`, **and** the settle condition (§5.2) | The latch. |
| T13 | Slide | Tuck | the same exits, settle condition **not** met | Button still held, so Normal is forbidden by §3.5's law V1. |
| T14 | DuckWalk | — | `¬H` | **No transition.** The duck walk is latched. |
| T15 | Normal, grounded | Skid | `ShouldEnterSkid` ∧ `Verb = Normal` | Shipped, plus the new `Verb` refusal. |
| T16 | Skid | Slide | T10 or T8 fires while `SkidRemaining > 0` | Sets `SkidRemaining = 0`. Velocity untouched — this is §8, complete. |
| T17 | Skid | Normal | the four shipped exits | Unchanged. |
| T18 | Airborne | Airborne | `E` ∧ `AirJumpsUsed < AirJumpCountMax` ∧ `AirJumpMode > 0` ∧ mode conditions | §7. Not a state change — a velocity event inside state C. |
| T19 | Slide | Airborne | `¬Grounded` (ran off an edge) | T2. You fly off carrying the slide's speed. Emergent, free, wanted. |

**No `TBD`, no blank cell.** Every state in §3.2 appears as both a `From` and a `To` except
Airborne-with-no-verb, whose only exits are T7/T8.

### 3.4 Resolution order — one pass, fixed, deterministic

MECHANICS §3 wants a fixed order rather than whichever system runs first. This is it, evaluated
once per tick inside `Step`, before the horizontal block:

1. **Control lock / ballistic** (existing `ControlLockedBy`, `BlastBallistic`). T1.
2. **Grounded test.** If `¬prev.Grounded`: `Verb = Normal`, `VerbClockTicks = 0`,
   `ChainTimerTicks` does not decrement. T2.
3. **Jump resolution.** Existing `StepJump`, unchanged, then the air jump (§7). If a jump fires:
   `Verb = Normal`, `VerbClockTicks = 0`, chain updated (§6.3). T4/T5/T18.
4. **Verb transition.** Evaluated **only if no jump fired and the body is grounded**. T3, T7–T14.
5. **Skid.** Existing `StepSkid`, evaluated **after** the verb, and forced to `0` if
   `Verb ≠ Normal`. T15–T17.
6. **Horizontal integration**, using whichever authority the resolved `Verb` names.

**The seven simultaneous cases, each with its stated winner and why:**

| Case | Winner | Why |
|---|---|---|
| Jump edge + verb entry, same tick | **jump** | Rule 3 before rule 4. The jump is the one input carrying a hard responsiveness guarantee; a verb that could swallow a jump press would cost a frame, which is forbidden. **This is the most important precedence in the table.** |
| Touchdown + live jump buffer + `H`, same tick | **the buffered jump** | And it cannot actually arise: the buffer is armed only by a press *edge*, and a held button produces no edge (MOVE-3a §6.2, shipped). Stated as a proof so nobody adds a branch for it. |
| Slide speed-exit + release, same tick | **release** → Normal | An implicit settle must never override an explicit input. |
| Slide min-duration + max-duration | cannot overlap | Enforced as a coupled invariant, `SlideMinSec < SlideMaxSec` (§11.3). |
| Chain timer reaches 0 + a chained jump fires, same tick | **the jump** | The grace is read in rule 3; the decrement happens in rule 4. Fixed order, so the inclusive boundary is deterministic on server, owner and replay alike. |
| Slide entry + skid entry, same tick | **slide**, skid suppressed | Deliberate intent beats automatic consequence. T16 — and this is the case that carries §8's momentum share. |
| Leaving the floor while sliding | **airborne** | T2/T19. Velocity untouched, so the slide's speed becomes the jump's. Free and wanted. |

### 3.5 The flicker analysis — MECHANICS §2 against adjacent-tick churn

The classic failure the packet names is "a slide that can start and stop at 60 Hz on a
boundary". Two laws make it impossible, and then every pair is checked with numbers.

> **Law V1. While `Grounded ∧ JumpHeld` and the hold window has elapsed, `Verb` is never
> `Normal`.**
> **Law V2. `VerbClockTicks` is reset to 0 on any tick where `¬JumpHeld`.**

V2 is the anti-chatter mechanism for the button axis: a release does not merely exit, it
**spends the window**, so re-entry costs a full fresh `JumpHoldWindowSec` — 12 ticks at the
default. V1 is what stops a Slide that runs out of speed from falling back into Normal while the
button is still down, which is the exact oscillation this analysis exists to prevent (it would
not have been a 60 Hz strobe; it would have been a 5 Hz one, which is worse because it looks
tuned rather than broken).

| Pair | Minimum round trip | Why |
|---|---|---|
| Normal ↔ Slide | **≥ 0.63 s** at the worst tuning; **≥ 1.3 s** at defaults | Entry needs `v ≥ 6.588`; exit at `v ≤ 2.00`. The **4.588 m/s gap** must be crossed down at `SlideDeceleration` (≤ 0.115 s at the max 40 m/s², 0.765 s at the default 6.0) **and** back up at `Acceleration` = 9 (0.510 s). Plus V2's 12-tick window. `ARITHMETIC`. |
| Normal ↔ Tuck | **≥ 12 ticks + a release** | V2. |
| Slide ↔ DuckWalk | **no cycle exists** | T12 is one-way; the only DuckWalk exit is a press edge, which goes to Airborne (T5), and the only Slide entry is from Normal or Airborne. Structural, not numeric — no tuning can create the edge. |
| Tuck ↔ Slide | **no cycle exists** | The Tuck's wish is capped at `DuckWalkSpeedFraction × MoveSpeed`; but more strongly, **there is no Tuck → Slide row in §3.3 at all.** |
| Skid ↔ Slide | **no cycle exists** | Slide entry cancels the skid (T16); skid entry refuses while `Verb ≠ Normal` (T15). One-way in both directions. |
| Airborne ↔ any verb | **the verb ends, it never strobes** | V0 forces Normal on every airborne tick, and re-entry costs 12 held ticks. A chattering `Grounded` therefore produces "the slide won't start", which is visible and harmless. |
| `ChainDepth` ± | **≥ 21 ticks between a decrement and any other chain event** | It increments only on a jump tick (≥ 1 airborne tick between any two) and decrements only on a timer expiry (≥ 21 ticks). `ARITHMETIC`. |
| `AirJumpsUsed` ± | **no cycle within one flight** | Increments only on an air press edge; resets only on a grounded tick. |

**MECHANICS §2.5 — the player can tell which state they are in.** Tuck and DuckWalk are the two
states a player can sit in for more than a moment, and they have *identical locomotion*, so
§2.5's "distinguishable from each other" is a real requirement here and not a formality. §12
gives them opposite poses: the Tuck is **braced** (compressed, arms in, the button is being
held), the DuckWalk is **settled** (extended, head lowered, walking). The visual difference is
the same difference the input has, which is the cheapest kind of legibility there is.

---

## 4. The crouch-slide

### 4.1 The one mechanism that was rejected, and why it matters

The brief reads as a **charge**: hold, release within a window, get a bigger jump. The obvious
implementation is a deferred launch — the jump fires on release, and holding past the window
cancels it into a slide. **It is rejected outright**, because a tap would then fire on release
rather than on press, which at human tap speed is 3–6 ticks of latency on the single most
common input in the game. "Weight is skin, not friction — nothing about weight may ever cost the
player a frame of input responsiveness" outranks every mechanic in this document, so the
mechanic is the thing that changes.

What survives is better, and it is already shipped: **the "window" is the rise.** The press
launches at full `JumpVelocity` with zero latency; releasing during the 0.382 s rise cuts the
arc through `JumpReleaseGravityMultiplier`; holding through the rise gives the tall jump. Height
is tied to hold duration, continuously and monotonically (MOVE-3a §3.3). *That is the charged
jump the brief asks for, and Talon has already played and approved it.*

The slide therefore attaches to the **other** side of the same hold: still holding at touchdown.

### 4.2 The crouch trigger

```
crouchTrigger := Grounded ∧ JumpHeld ∧ Water == Dry ∧ no jump fired this tick
                 ∧ VerbClockTicks ≥ round(JumpHoldWindowSec × 60)
```

`VerbClockTicks` increments by one on every tick where `Grounded ∧ JumpHeld ∧ Verb == Normal ∧
no jump fired`, and is reset to 0 whenever any of those is false (law V2). At
`JumpHoldWindowSec = 0.20` that is **12 ticks**. `ARITHMETIC`.

The trigger then branches on ground speed, and **both branches land in a defined state**:

- `v ≥ SlideEnterSpeedMps` → **Slide** (T10)
- `v < SlideEnterSpeedMps` → **Tuck** (T11)

**Boundary (MECHANICS §1):** inclusive at the entry speed, matching `ShouldEnterSkid`'s
`speed >= SkidEnterSpeedMps` exactly, so "at or above" means one thing in the motor. At exactly
the threshold you slide. There is no input that produces nothing.

### 4.3 The entry speed — its own knob, not the skid's, and why

**Item 2's question, answered: the slide does NOT reuse `SkidEnterSpeedMps = 4.05`.** It gets
`SlideEnterSpeedFraction`, a fraction of `MoveSpeed` exactly as knob 19 is (MOVE-4a §2.5 — the
derivation is load-bearing, so the field is the fraction and the m/s is the product), defaulting
to **1.22 → 6.588 m/s**.

Three reasons, in order:

1. **They answer different questions.** `SkidEnterSpeedMps` asks *"did you commit hard enough to
   a direction that reversing it should cost you?"* The slide asks *"are you moving fast enough
   that a slide will read as a slide?"* 4.05 m/s is **below jog** (5.40); a slide entered at a
   brisk walk would decelerate over 0.34 s and 0.7 m, which is a stumble, not a slide.
2. **1.22 is not an arbitrary number — it is `LocomotionProfile.SprintEnterMps`.**
   `JogSpeedMps × 1.22 = 6.588 m/s` is the exact threshold at which the shipped gear machine
   already calls the body *sprinting*. So the rule has a name a player can hold: **you can only
   slide out of a sprint.** One line in the world, two systems agreeing, no second number to keep
   in step — the same coherence MOVE-3a §2.4 prized when it noticed the air-brake boundary
   landing just above the skid threshold.
3. **Coupling them would couple two unrelated experiments.** The slide's decel and the skid's
   decel are different numbers with different jobs; a shared entry would mean tuning one verb
   silently retunes the other, which is precisely the failure MOVE-4a built the invariant
   readout to catch.

**Anti-chatter, the skid's own law applied:** `SlideExitSpeedMps` (default **2.00 m/s**, just
below walk speed 2.43) must stay strictly below the entry product. Gap at defaults: **4.588 m/s**.
This goes into `MotorTuningInvariants` as a coupled constraint, exactly like
`SkidExitSpeedMps < SkidEnterSpeedFraction × MoveSpeed` already is.

**The knock-on that must be stated:** at fraction 1.22, a *jog* jump held all the way lands at
exactly `MoveSpeed` = 5.40 m/s (the airborne ceiling makes the `MoveToward` a no-op, so the
landing speed is exactly 5.40, bit-for-bit, on server and client alike) — **below** 6.588, so a
held jog jump lands in a **Tuck**, not a slide. Only sprint slides. `ARITHMETIC`. Whether that
reads well is `FEEL — UNVERIFIED`.

### 4.4 The slide's own behaviour

Three rules, and the third is the one that makes it a slide rather than a skid:

1. **Speed decays at `SlideDeceleration` (default 6.0 m/s²) along the current heading.** Lower
   than ground `Deceleration` (21) and lower than `SkidDeceleration` (13) — a slide is
   *slippery*, and that is the whole verb.
2. **The stick contributes no acceleration and no braking.** You cannot pump a slide and you
   cannot shorten one by letting go of forward. `SlideDeceleration` is the single speed
   authority in this state.
3. **The heading rotates toward the stick at `SlideTurnRateDeg` (default 90 °/s).** A *rotation*
   of the velocity vector, not a `MoveToward` — so the carve preserves speed exactly and cannot
   fight rule 1. This is the carve the brief's stretch goal asks for, and it is what distinguishes
   the slide from the skid (which suppresses steering entirely).

**What that produces** — `ARITHMETIC`:

| Entry speed | Time to `SlideExitSpeedMps` | Distance | Total carve at 90 °/s | Carve per metre |
|---|---|---|---|---|
| sprint landing, **8.64 m/s** | `(8.64−2.00)/6.0` = **1.107 s** | `(8.64²−2.00²)/12` = **5.888 m** | **99.6°** | 16.9 °/m |
| minimum entry, **6.588 m/s** | **0.765 s** | **3.283 m** | **68.8°** | 21.0 °/m |

A sprint slide is 5.89 m long — within 5% of a sprint jump's 6.19 m — and can round a right-angle
corner with 10° to spare. A minimum-entry slide can only adjust a line. **The faster slide turns
*less* per metre**, which is the weighty reading of "speed is the dial" and the same shape SKID-1
already uses. `FEEL — UNVERIFIED`.

**Facing:** travel, not wish, exactly as the skid does (`ResolveYaw` handed the velocity).

### 4.5 The two duration bounds, and why neither is a lockout

- **`SlideMaxSec` = 1.40 s.** At the defaults it **never fires** (1.107 s < 1.40 s) — it is the
  same kind of guard `SkidMaxSec` is. It earns its keep only at the edge of the knob range: at
  `SlideDeceleration = 0.5` (the hard minimum) the speed exit would take 13.3 s, and without the
  cap that is a state with no exit reached by a slider, which MECHANICS §2 forbids and MOVE-4a
  §2.3 already established as a bound worth making structural.
- **`SlideMinSec` = 0.12 s (7.2 ticks).** It blocks **only the speed and duration exits**, never
  the release exit. *Releasing the button always exits on the next tick, from any state, at any
  time.* That is what makes this bound legal under the weight principle: it is not a commitment
  window, it is a floor on the *self*-termination, and the player can always leave. It too is
  inert at the defaults (the speed exit cannot fire before 0.765 s) and lives for the range edge:
  at `SlideDeceleration = 40` the speed exit would fire in 0.115 s, and on a downslope sooner
  still. 0.12 s deliberately equals `CoyoteTimeSec` and `JumpBufferSec`, so the body has one
  forgiveness constant rather than three.

### 4.6 Slide → jump, and slide → ledge

Both fall out; neither is authored.

- **The slide-jump.** Releasing exits the slide (T9); the fresh press then jumps, and with a
  0.12 s jump buffer a release-and-press inside one window puts the jump on the very next
  eligible tick. The jump tick does not touch horizontal velocity and `LandingSpeedMultiplier`
  is 1.0, so **you launch at whatever the slide had left**, and §9's airborne ceiling holds it.
  A jump taken 0.4 s into a sprint slide launches at `8.64 − 6.0 × 0.4` = **6.24 m/s**.
  `ARITHMETIC`. That is a slower jump than a sprint jump, which is correct — you spent speed to
  get low.
- **The slide off a ledge.** T19/T2. `Verb → Normal`, velocity untouched, so the slide becomes a
  jump-shaped fall carrying the slide's exact speed.

### 4.7 Water — a guard that would otherwise have shipped a bug

`WaterGeometry.JumpAllowed` denies the jump while swimming. A wading player holding the jump key
is therefore **grounded, holding, and no jump fires** — which is the crouch trigger's exact
precondition, and the body would tuck in the shallows. **T3 forbids every crouch verb unless
`Water == Dry`**, and forces `Verb = Normal` the tick the water state changes. Stated because
this is a bug that arrives silently and reads as a physics glitch.

---

## 5. The stationary crouch-tuck and the duck walk

### 5.1 They share one behaviour and differ only in the latch

Both cap the wish speed at `DuckWalkSpeedFraction × MoveSpeed` and change nothing else. They are
two enum values, not two behaviour functions. The difference is the exit rule: **the Tuck is
held; the DuckWalk is latched.**

That is not a compromise — it is the brief's own word. "Hold forward through the slide →
**settles** into a duck walk," and "the player may remain in duck walk **indefinitely**." A
state you may remain in indefinitely cannot require a held button, and a state you pop out of on
release must. **The settle is the latch.**

### 5.2 The settle condition, and the guaranteed/not-guaranteed toggle (item 3)

A Slide that ends by its **speed** or **duration** exit becomes a DuckWalk if:

```
DuckWalkGuaranteed = 1  →  always
DuckWalkGuaranteed = 0  →  the stick is held within DuckWalkEntryConeDeg of the travel direction
```

Otherwise it becomes a **Tuck** (T13) — never Normal, because the button is still down and law
V1 forbids Normal-while-held.

**Default: `DuckWalkGuaranteed = 0`, cone 60°.** Argued: the brief's sentence is "hold *forward*
through the slide", and "forward" is a condition. The guaranteed variant deletes the condition;
shipping the brief's literal reading as the default and building the other is what the packet
asks for. `FEEL — UNVERIFIED`: whether a 60° cone is generous enough that a player steering out
of a carve still settles is a hands question. The cone is a knob for that reason.

### 5.3 The duck walk's speed

`DuckWalkSpeedFraction` default **0.45 → 2.43 m/s**, which is **exactly
`LocomotionProfile.WalkSpeedMps`**. Argued: the game already has a name for 2.43 m/s, the gear
machine already has hysteresis around it, and a fourth speed in a three-gear world is a number
nobody can hold. More importantly, **the duck walk costs no speed at all relative to a walk** —
"weight is skin, not friction" means the crouch's cost is its pose and its low profile, not a
tax, and the brief calls it a *legitimate persistent traversal option*, which a taxed state is
not.

**Recommended first experiment** (MOVE-4a's practice — a named experiment, not a moved default):
drag it to **0.30 → 1.62 m/s** if the duck should feel like it costs something. `FEEL —
UNVERIFIED` either way.

### 5.4 Jumping out of the duck walk — decided (item 3)

**Decision: yes. A `Jump` press edge exits the duck walk and fires the jump on the same tick. No
stand-first, no intermediate state, no delay.**

Argued:

1. **The principle forbids the alternative.** A required stand-first is a recovery lockout by
   another name — it costs frames on the game's most important input, and there is no recovery
   lockout, ever.
2. **It is free.** `StepJump` already fires on the press edge and does not consult `Verb`. The
   only work is §3.4 rule 3 forcing `Verb = Normal` on the jump tick, which V0 would do one tick
   later anyway.
3. **The jump *is* the stand-up**, so one input does both and there is no ambiguity about what
   the press meant.

**What it costs if this is wrong.** The player has no way to stand up *without* jumping — a
stationary duck-walker who wants to stand takes a 1.02 m hop to do it. If Talon wants a silent
stand, the options are a second binding (one free bit in `buttons2`, **no length change, no extra
protocol bump** — see §10) or a tap/hold split on the jump button, and the second is refused on
sight because a hold-to-stand costs frames. Named as Open question O2 rather than guessed at.

### 5.5 The capsule does not change

**No collision-capsule change in this wave.** The crouch is a pose: `AvatarVisual` compresses the
body, `SandboxAvatar`'s `CapsuleShape3D` does not move. Argued:

1. **Nothing in the playground is low enough for a shorter capsule to reach.** MOVE-3a §8's
   element list has no overhead the player is meant to pass under.
2. **Growing a capsule needs a headroom check whose failure state is "cannot stand"** — a state
   with no exit if the player also cannot move out from under. Building that machinery for a lab
   with no ceilings is cost with no return, and MECHANICS §2 would then own the result.
3. **"Weight is skin."** The crouch's read is the pose.

**Trigger to revisit:** the first authored overhead a player is meant to pass under. At that
point the capsule swap, the headroom check and the blocked-stand state all get designed
together, which is one coherent package rather than three accidents.

---

## 6. The chain jump

Distance, never height. Generous, not precision-timed. Gradual decay. Animation tell only, no UI
of any kind.

### 6.1 The model that survives the shipped air code — and the one that does not

**The obvious model is wrong, and the arithmetic says so.** Adding a horizontal *velocity* bonus
on the jump tick does not survive the flight. MOVE-3a §2.3's airborne clause is

```
desired = Min(desired, Max(flatSpeed, groundWish))
```

so with `desired` = the sprint wish 8.64 and `flatSpeed` = 9.24 after a bonus, the wish resolves
to **8.64** and `MoveToward` **brakes the body back down** at
`AirControlBuild × Acceleration = 0.45 × 9 = 4.05 m/s²`. Over a 0.717 s flight that sheds up to
2.90 m/s — the whole bonus, and then some. **A velocity-boost chain lands at exactly the speed it
took off at.** `ARITHMETIC`.

**The model that works: the chain raises the *wish speed*, not the velocity.** It is a gear above
sprint that you earn:

```
speed += ChainDepth × ChainBonusMps        // applied to the wish, grounded AND airborne
```

One term, one place, computed from a field already in `MoveState`. Grounded, it lets you
accelerate past 8.64; airborne, it raises `desired` so the ceiling clause preserves rather than
brakes. **Nobody gains speed in the air** — the depth can only change on a jump tick, which is
grounded by definition — so §2.3's actual guarantee is untouched. §9 states the amendment.

### 6.2 The ladder

`ChainDepth` counts *consecutive jumps beyond the first*: hop 1 is depth 0, hop 2 is depth 1.
Reaching the cap takes five hops, which is what makes a chain something you build.

`ARITHMETIC`, held sprint jumps, airtime 0.7167 s, `ChainBonusMps = 0.60`, ground rate 9 m/s²,
air rate 4.05 m/s², one grounded tick between hops (the buffered chain, MOVE-3a §6.2):

| Hop | Depth | Wish | Speed at launch | Distance | vs baseline |
|---|---|---|---|---|---|
| 1 | 0 | 8.64 | 8.64 | **6.192 m** | — |
| 2 | 1 | 9.24 | 8.64 → ramps | **6.578 m** | +6.2% |
| 3 | 2 | 9.84 | 9.39 | **7.028 m** | +13.5% |
| 4 | 3 | 10.44 | 9.99 | **7.458 m** | +20.4% |
| 5 | 4 (cap) | 11.04 | 10.59 | **7.888 m** | +27.4% |
| 6+ | 4 | 11.04 | 11.04 | **7.912 m** | +27.8% |

Top speed **11.04 m/s = 1.28 × sprint**. The ramp inside each flight is real and is included: a
new gear takes `0.60 / 4.05` = 0.148 s of airtime to actually reach.

**Two design facts this hands Talon, both worth knowing before he drags the slider:**

- **The chain converts MOVE-3a §8.3's "honest wall" into a skill gate.** The 6.6 m gap, built to
  be impossible, is cleared at **chain depth 2**. The 5.8 m precision gap is cleared at depth 1
  with 0.78 m of margin instead of 0.39 m.
- **Tap chains and hold chains reach the cap at different rates.** A held hop cycle is
  0.733 s, so five hops take 2.93 s; a tapped hop cycle is 0.333 s, so five hops take **1.33 s**.
  The tap chain is the *faster* chain and the hold chain is the *longer* one — exactly the two
  rhythms MOVE-3a §8.8 already built a course for, now with a reason to prefer one. `ARITHMETIC`.

### 6.3 Accrual

On any tick a jump fires (ground or air):

- if `ChainTimerTicks > 0` and `ChainDepth < ChainMaxDepth` → `ChainDepth++`
- if `ChainTimerTicks > 0` and `ChainDepth == ChainMaxDepth` → unchanged (the cap)
- if `ChainTimerTicks == 0` → `ChainDepth = 0`, and this jump *starts* a chain
- in all cases the timer is set to the grace on the following touchdown (§6.4)

### 6.4 The window, the decay, and one byte that does both

**One counter, `ChainTimerTicks`, with one meaning: ticks until the next depth decrement.**

- On the tick `Grounded` returns true, it is set to `round(ChainGraceSec × 60)` = **21 ticks**.
- It decrements by 1 on every **grounded** tick. **It does not decrement while airborne** — a
  long flight can never break a chain, which is the athletic reading (you do not stop between
  bounds) and it means the chain measures *ground dwell*, the thing the player actually controls.
- At 0: `ChainDepth--`, and the counter resets to `round(ChainDecayIntervalSec × 60)` = **30
  ticks**. At depth 0 it stops.

**"Generous", as a number.** 21 ticks of ground dwell is **0.35 s**, which at sprint is **3.02 m
of running between hops**. It is 2.9× the jump buffer (0.12 s) and 4.4× what a precision-timed
chain would be (~0.08 s). A player who mashes gets it for free; a player who lands, takes a
stride, and jumps again still gets it. `ARITHMETIC` for the numbers, `FEEL — UNVERIFIED` for
whether 0.35 s reads as generous.

**The decay curve: linear in depth, one level per 0.50 s.** From the cap, full loss takes
`0.35 + 3 × 0.50` = **1.85 s** (111 ticks). Fumble one landing and re-chain inside half a second
and you lose exactly one level. That is "decays gradually, not reset to zero" with a shape a
player can feel and the tell can show one step at a time.

**Boundary (MECHANICS §1):** a jump chains if `ChainTimerTicks > 0` **at the moment rule 3 reads
it** — inclusive of tick 1, exclusive of 0. The decrement happens in rule 4, after. Fixed order,
identical on server, owner prediction and replay.

**Interaction with the slide, stated:** a slide is grounded dwell, so it burns the chain — a
1.107 s sprint slide costs two levels. You cannot bank momentum in both currencies at once. The
slide-jump out the far end starts a fresh chain. `ARITHMETIC`.

### 6.5 The charged jump × the chain jump — decided (item 4)

**Decision: they stack, freely. They are orthogonal by construction and no gate is added.**

Argued:

1. **They are literally different terms in the same product.** Distance = speed × airtime. The
   charged (held) jump buys airtime; the chain buys speed. Separating them would require an
   artificial rule the player must be *told* rather than feel.
2. **The two rhythms are the reward.** §6.2's tap-vs-hold arithmetic gives a fast shallow chain
   and a slow long one; a mutual exclusion would collapse them into one.
3. **A gate would say something incoherent** — that the button which makes you tall makes you
   slow, on the same press.

**The cost, named rather than smuggled:** a held, max-chain sprint jump is **7.91 m long and
1.53 m tall**, and a tapped max-chain jump is `11.04 × 0.3167` = **3.50 m** long and 0.47 m tall.
7.91 m is the longest jump reachable without the double jump, and it will clear things sized for
6.19 m. `ARITHMETIC`.

### 6.6 No UI. Ever.

**The chain has no numeric readout, no bar, no icon, no text, no HUD element of any kind, in any
build, including the lab.** §12 is the entire communication channel. This is stated as a
prohibition rather than an omission so that a debug readout does not arrive later as a
convenience and stay.

---

## 7. The double jump — two variants, one toggle, shipped off

`AirJumpMode` (knob 46): **0 = off (default), 1 = traditional, 2 = the Kick.**

Ships at 0 — the exact no-op — for MOVE-4a's reason: a double jump changes what every gap in the
playground means, and the baseline Talon approved is the only baseline the lab has. He switches
it on to feel it, which is literally what he asked for.

`AirJumpsUsed` resets to 0 on any grounded tick. Both variants require
`AirJumpsUsed < AirJumpCountMax` and a `Jump` press edge while airborne.

### 7.1 Variant 1 — traditional

```
velocity.Y = JumpVelocity × AirJumpVelocityFraction        // 8.4 × 0.80 = 6.72 m/s
AirJumpsUsed++
```

`ARITHMETIC`. Second-jump apex from 6.72 at `Gravity = 22`: `6.72² / 44` = **1.026 m**. Taken at
the apex of a held sprint jump (1.534 m), total peak **2.560 m**, total airtime
`0.382 + 0.306 + 0.415` = **1.103 s**, horizontal reach at sprint **9.53 m**.

**Why it reads floaty — the tension Talon named, stated precisely so the alternative can answer
it:** `velocity.Y` is *overwritten*, so all accumulated downward momentum is deleted in one
frame. That is the single most weightless thing a body can do. It also works at zero horizontal
speed and at any moment of the fall, so it functions as a mid-air undo — "I misjudged and the
game gave it back" — which is the reading that costs the body its weight.

### 7.2 Variant 2 — **the Kick.** A named behaviour, with numbers.

> **The Kick is not a second launch. It is a drive.** The body snaps into a pike, kicks its legs
> down and back, and converts the fall it has already accepted into forward speed. It can only be
> taken **while descending**, and only **above a committed run speed**. It buys distance, not
> height.

Conditions — all three, each load-bearing:

```
velocity.Y < 0                                  // descending: you must already have paid the height
flatSpeed >= KickMinSpeedMps                    // 4.05 — a committed run
AirJumpsUsed < AirJumpCountMax
```

On fire:

```
gain      = KickHorizontalGainMps + |velocity.Y| × KickConversionFraction
newSpeed  = max(flatSpeed, sprintWish + gain)   // a TOP-UP to a ceiling, never a compound add
flatSpeed = newSpeed                             // along the CURRENT heading; the stick cannot turn it
velocity.Y = KickVerticalMps                     // 1.60 — arrests the fall, does not climb
AirJumpsUsed++
```

`sprintWish` is the body's own current sprint wish, `MoveSpeed × SprintMultiplier +
ChainDepth × ChainBonusMps`.

**Defaults:** `KickConversionFraction` **0.35**, `KickHorizontalGainMps` **1.20**,
`KickVerticalMps` **1.60**, `KickMinSpeedMps` **4.05** (which equals `SkidEnterSpeedMps` at the
shipped tuning — deliberately the same "committed run" line MOVE-3a §2.4 already made a rule; it
is a separate knob rather than a derived one because MOVE-4's table is one float per row).

**Worked example** — held sprint jump, Kick pressed 0.20 s past the apex. `ARITHMETIC`:

| | |
|---|---|
| `v_y` at the press | `−29.7 × 0.20` = **−5.94 m/s** |
| gain | `1.20 + 0.35 × 5.94` = **3.279 m/s** |
| speed after | `max(8.64, 8.64 + 3.279)` = **11.919 m/s** |
| height at the press | `1.534 − ½ × 29.7 × 0.04` = **0.940 m** |
| rise bought by `KickVerticalMps` | `1.60² / 44` = **0.058 m** over 0.073 s |
| peak after | **0.998 m** |
| fall from 0.998 m at 29.7 | **0.259 s** |
| **total airtime** | `0.382 + 0.200 + 0.073 + 0.259` = **0.914 s** |
| **total distance** | `8.64 × 0.582 + 11.919 × 0.332` = `5.027 + 3.957` = **8.98 m** |

**Against the traditional double jump: 8.98 m at a 1.00 m peak, versus 9.53 m at a 2.56 m peak.
94% of the reach at 39% of the altitude.** That one line is the variant's whole thesis.

**Why the Kick does not read floaty — the argument AC5 asks for, five points:**

1. **It cannot save you.** It needs `v_y < 0` *and* ≥ 4.05 m/s of horizontal speed, and it grants
   +1.60 m/s of vertical — enough to flatten an arc, never enough to recover a misjudged one. The
   traditional double jump's defining floaty read is the mid-air undo, and the Kick structurally
   cannot perform it.
2. **It spends; it does not create.** The gain is 35% of a fall you had already accepted. The
   player pays height for distance at a losing exchange rate. Nothing is conjured.
3. **It commits harder, not less.** The gain goes along the *current heading only* — the stick
   cannot turn it — so the Kick makes the arc **more** ballistic. Mid-air control is what reads as
   light; the Kick removes some.
4. **It has a floor you can be below.** Under 4.05 m/s it does nothing at all, so a standing hop
   has no second jump. The traditional variant works best at zero speed, which is exactly where it
   reads worst.
5. **The pose is a push, not a leap** (§12.4). The body looks like it pushed against something.
   Clip-safe under CANON §0.8: it is a slapstick pike, no gore, nothing taken.

**The runaway the `max` prevents, stated because the naive version is an exploit.** With a
compounding `flatSpeed += gain`, each flight adds up to `1.20 + 0.35 × 9.55` = **4.54 m/s** while
one grounded tick costs only 0.15 m/s — speed grows without bound. The `max(flatSpeed,
sprintWish + gain)` formulation caps the Kick's *output* at `sprintWish + gain`, so repeating it
does not take you higher: **the Kick is how you reach the Kick's speed, not how you exceed it.**
`ARITHMETIC`.

**The composed ceiling, so Talon knows it before he switches things on.** At `ChainDepth = 4` the
sprint wish is 11.04, so the Kick ceiling is `11.04 + 4.54` = **15.58 m/s = 1.80 × sprint**, and
the deepest jump in the whole spec reaches roughly **11.5 m** horizontally. Bounded by
`ChainMaxDepth` and `AirJumpCountMax`, both knobs, both defaulted low. `ARITHMETIC`. It gets an
invariant readout row rather than a fourth knob, so it cannot drift out of step.

**One honest gap.** The Kick is `FEEL — UNVERIFIED` in the strongest sense: it is a behaviour
nobody has felt, argued from arithmetic and from a stated theory of why double jumps read light.
It is specified concretely enough to build and to switch between, which is exactly what the brief
asked for and exactly as far as paper can take it.

---

## 8. The skid–slide momentum share

### 8.1 The share itself is one line

**Entering `Slide` while `SkidRemaining > 0` sets `SkidRemaining = 0` and touches nothing
else.** (T16.) The velocity vector — magnitude, direction, and every bit of sideways component
the skid was carrying — is untouched, and the slide then decelerates it at 6.0 instead of 13 and
begins its carve from the skid's heading.

There is no momentum to *transfer*, because there is only one velocity vector and two states
that treat it differently.

### 8.2 The network difficulty was priced against a different architecture

The brief calls this "genuinely hard given the server-authoritative + prediction setup."
**Respectfully: in this repo it is not, and saying so is more useful than agreeing.**

Every quantity involved — `Velocity`, `SkidRemaining`, `Verb`, both clocks — lives in
`MoveState`, is produced by a pure function of the previous state and this tick's intent, and is
replayed identically by the server, by owner prediction and by a reconciliation rewind. The thing
that makes momentum sharing hard elsewhere is a physics impulse applied by an event *outside* the
deterministic step; that cannot happen here, because `AvatarMotor.Step` is the sole transform
writer and this package ships no `PhysicalBone3D` ragdoll at all (`Step`'s own physics-authority
comment; `STATE-CASCADE-TABLE.md` hard constraint 1). The share costs **zero wire bytes and zero
new determinism risk**.

### 8.3 What *is* hard is the entry — build the share, defer the entry

The fluid input the brief describes — *skid around a corner, then hold jump to drop into a
slide* — cannot be reached under ruling 1. You are grounded and skidding; you press jump; the
press fires a jump with zero latency (§2); you leave the floor, `StepSkid` returns 0, and the
skid's sideways momentum is gone 0.7 s before you land.

**Recommendation:**

- **Build the share now** (the one line, T16). It costs nothing and it is the correct behaviour
  the moment a slide ever *does* begin with a skid timer live — which the touchdown path can
  produce today: a landing at speed into a reversed stick starts a skid on the touchdown tick
  (MOVE-3a §6.1's land-and-slide), and `TouchdownSlideImmediate = 1` starts the Slide on that same
  tick. T16 is the rule that decides that collision, and it must exist regardless.
- **Defer the entry.** The blocker is not the network and not the momentum; it is that one
  zero-latency button cannot also mean "slide from the ground".

**The trigger for revisiting, named:** Talon plays the `TouchdownSlideImmediate` knob and says
whether a jump in front of every slide is acceptable. If it is not, the fix is Open question O1's
second binding — **one free bit in `buttons2`, no length change, and no protocol bump beyond the
one this wave already pays** (§10) — and the skid→slide conversion becomes reachable in the same
wave that adds it. **What must be true first is a hands verdict, not a technical prerequisite.**

---

## 9. The one amendment this wave requires

Out of scope forbids retuning MOVE-4 and says a required change is a finding, not an edit. This
is that finding, and it is the only one.

**MOVE-3a §2.3's airborne wish ceiling needs one added clause, or neither the chain jump nor the
Kick can exist.** The arithmetic is in §6.1: the shipped clause resolves `desired` to the
*unchained* sprint wish and then brakes the body down to it at 4.05 m/s², eating any earned speed
inside a single flight.

**The amendment — "the momentum grant":**

```
desired = Min(desired, Max(flatSpeed, groundWish));           // shipped, unchanged
if (ChainDepth > 0 || AirJumpsUsed > 0)                        // NEW
    desired = Max(desired, flatSpeed);                         // hold what you have; never gain
```

- It **never lets a body gain speed in the air.** `Max(desired, flatSpeed)` can only stop a
  brake; it cannot exceed the speed already carried. §2.3's actual guarantee — *nobody gains
  speed in the air* — is strengthened in letter and kept in spirit.
- It is **gated on two fields already in `MoveState`**, so it is a pure function of replicated
  state and adds no wire cost and no determinism risk.
- **At the shipped defaults it is the exact no-op**: `ChainBonusMps = 0.00` means `ChainDepth`
  never leaves 0, and `AirJumpMode = 0` means `AirJumpsUsed` never leaves 0. The body Talon
  approved is bit-identical until he drags a slider. That is MOVE-4's own discipline, applied to
  a clause instead of a constant.

**No other MOVE-3 or MOVE-4 value or clause changes.** `LandingSpeedMultiplier` stays 1.0 and the
chain jump depends on it (a landing tax would compound across a chain, which is MOVE-3a §6.1's
own argument, now with a feature that proves it).

---

## 10. The wire

### 10.1 Per state and counter

| State / counter | On the wire? | Cost | Why |
|---|---|---|---|
| `Verb` (Normal / Tuck / Slide / DuckWalk) | **yes**, `MoveState` | **2 bits** | Changes wish speed, decel authority and steering. `STATE-CASCADE-TABLE.md` hard constraint 1: a replay starting from a different verb resolves a different trajectory and re-diverges forever — the identical argument `SkidRemaining` makes. |
| `VerbClockTicks` | **yes**, `MoveState` | **1 byte** | One shared clock: the hold window, the slide's min and max. The three are mutually exclusive by construction, which is what makes one byte legal. |
| `ChainDepth` | **yes**, `MoveState` | **3 bits** | Changes the launch wish. Also drives §12's tell for free. |
| `ChainTimerTicks` | **yes**, `MoveState` | **1 byte** | Grace and gradual decay in one counter (§6.4). Live simultaneously with `VerbClockTicks` (the chain decays during a slide), so they cannot be folded — checked, not assumed. |
| `AirJumpsUsed` | **yes**, `MoveState` | **2 bits** | A replay must never re-grant a spent air jump. A counter, not a bool, so `AirJumpCountMax` can be a real knob. |
| the duck-walk latch | **no** | 0 | The latch *is* the enum value. |
| §9's momentum grant | **no** | 0 | Pure function of `ChainDepth`, `AirJumpsUsed` and velocity, all already present. |
| the skid–slide share | **no** | 0 | It is `Velocity`. Already replicated. |
| §12's animation tell | **no** | 0 | Reads `ChainDepth` and `Verb` off `MoveState`, exactly as the skid pose reads `SkidRemaining`. |

### 10.2 The totals

**Bits:** `2 + 3 + 2 = 7` → **one new snapshot flags byte (`flags2`), with one bit spare.** The
shipped flags byte at `span[5]` has been documented full since v10, so there was nothing to
economise; stating the spare bit here in the same words that byte uses is deliberate, so the
ninth flag's cost is discovered at design time rather than by finding 128 already taken.

**Bytes:** `flags2 (1) + VerbClockTicks (1) + ChainTimerTicks (1)` = **3 bytes.**

| Constant | Before | After |
|---|---|---|
| `NetCodec.SnapshotBytes` | 55 | **58** (+5.45%) |
| `NetCodec.InputEntryBytes` | 26 | **26 — unchanged** |
| `NetProfile.ProtocolVersion` | 12 | **13** |

**Zero new input bits.** Every verb in this document is carried by `MoveIntent.Jump` (edge),
`MoveIntent.JumpHeld` (level) and `MoveIntent.MoveDir` — all three shipped, all three already on
the wire. `buttons2` keeps its **7 free bits**. That is the wave's best wire news and it is a
direct consequence of ruling 1: one button costs one bit, and MOVE-3 already paid it.

**Bandwidth**, at `NetProfile.SnapshotIntervalTicks = 2` (30 Hz), one snapshot RPC per avatar
broadcast to every peer. `ARITHMETIC`:

- per replicated avatar stream: `3 B × 30 Hz` = **90 B/s**
- a 6-player session, server egress: `6 avatars × 30 Hz × 3 B × 5 recipients` = **2.64 kB/s
  added**, against a **48.3 kB/s** baseline for the same traffic
- per client ingress: `6 × 30 × 3` = **0.53 kB/s**

### 10.3 Why two clocks are quantized when `SkidRemaining` refused to be

`MoveState.SkidRemaining`'s own doc refuses a quantized byte, so quantizing here needs an
argument rather than a precedent.

**The two cases are different, and the difference is exact-versus-approximate.** `SkidRemaining`
is decremented by `dt` and compared against a *continuous* speed threshold, so it lands on
arbitrary fractions and a byte would round — and a rounded authoritative timer ends a skid one
tick early on the client and re-corrects the position. `VerbClockTicks` and `ChainTimerTicks` are
**integers by construction**: set once from a knob at `round(sec × TickRate)`, decremented by
exactly 1 per fixed tick, compared against integers. A byte holds them **exactly**, and is in
fact *more* robust than a float, which would accumulate `dt` addition error over a long duck
walk.

**The one dependency, stated so it cannot be broken silently:** this holds because `TickDelta`
is a fixed `1/60` on every peer. `TickDelta` is not a knob (MOVE-4a §2.4) and changing it is a
protocol change and a wave of its own. If it ever becomes variable, both bytes must become
floats.

### 10.4 If three bytes is too many

Stated as a real fork rather than a lament, because the packet is right that the cost may change
what is worth building:

- Dropping the **chain jump** entirely saves `ChainTimerTicks` (1 byte) and 3 bits — the flags
  byte would then hold 4 bits and the snapshot would be **57**. It does not save the flags byte
  itself, which `Verb` alone forces.
- Dropping the **double jump** saves 2 bits and no bytes.
- Dropping the **crouch family** saves the flags byte and `VerbClockTicks` — the snapshot returns
  to **55** and this wave has nothing left in it.

There is no cheaper encoding of what remains: both clocks are already at their exact minimum
form, and the flags are already packed to within one spare bit.

---

## 11. The knob table

MOVE-4a's shape, continued at row 31. Groups extend MOVE-4's seven (Ground, Gravity, Jump, Air,
Skid, Landing, Camera) with two new ones — **Slide** and **Chain** — and put the air-jump rows in
MOVE-4's existing **Air**. **MOVE-4's Skid group is not touched.** Owner is `AvatarMotor` for
every row.

### 11.1 The table

| # | Constant | Default | Unit | Min | Max | Step | Group | Pinned? |
|---|---|---|---|---|---|---|---|---|
| 31 | `JumpHoldWindowSec` | 0.20 | s | 0.05 | 0.60 | 0.01 | Slide | not pinned — keep free |
| 32 | `TouchdownSlideImmediate` | 0 | 0/1 | 0 | 1 | 1 | Slide | not pinned |
| 33 | `SlideEnterSpeedFraction` | 1.22 | × `MoveSpeed` | 0.50 | 2.00 | 0.01 | Slide | **will pin — coupled** |
| 34 | `SlideExitSpeedMps` | 2.00 | m/s | 0.10 | 6.00 | 0.05 | Slide | **will pin — coupled** |
| 35 | `SlideDeceleration` | 6.0 | m/s² | 0.5 | 40.0 | 0.5 | Slide | **min hard** (exitless state) |
| 36 | `SlideTurnRateDeg` | 90 | °/s | 0 | 360 | 5 | Slide | not pinned — keep free |
| 37 | `SlideMinSec` | 0.12 | s | 0.00 | 0.50 | 0.01 | Slide | **will pin — coupled** |
| 38 | `SlideMaxSec` | 1.40 | s | 0.10 | 3.00 | 0.05 | Slide | **will pin — coupled** |
| 39 | `DuckWalkSpeedFraction` | 0.45 | × `MoveSpeed` | 0.10 | 1.00 | 0.01 | Slide | not pinned — keep free |
| 40 | `DuckWalkGuaranteed` | 0 | 0/1 | 0 | 1 | 1 | Slide | not pinned |
| 41 | `DuckWalkEntryConeDeg` | 60 | ° | 0 | 180 | 5 | Slide | not pinned — inert at knob 40 = 1 |
| 42 | `ChainGraceSec` | 0.35 | s | 0.05 | 2.00 | 0.01 | Chain | not pinned — keep free |
| 43 | `ChainDecayIntervalSec` | 0.50 | s | 0.05 | 3.00 | 0.05 | Chain | not pinned — keep free |
| 44 | `ChainBonusMps` | **0.00** | m/s | 0.00 | 3.00 | 0.05 | Chain | not pinned — **0.00 is the exact no-op** |
| 45 | `ChainMaxDepth` | 4 | count | 0 | 7 | 1 | Chain | **max hard — wire width (3 bits)** |
| 46 | `AirJumpMode` | **0** | 0/1/2 | 0 | 2 | 1 | Air | **0 is the exact no-op** |
| 47 | `AirJumpCountMax` | 1 | count | 0 | 3 | 1 | Air | **max hard — wire width (2 bits)** |
| 48 | `AirJumpVelocityFraction` | 0.80 | × `JumpVelocity` | 0.10 | 1.50 | 0.05 | Air | not pinned — inert unless mode = 1 |
| 49 | `KickConversionFraction` | 0.35 | fraction | 0.00 | **1.00** | 0.01 | Air | **max hard** — inert unless mode = 2 |
| 50 | `KickHorizontalGainMps` | 1.20 | m/s | 0.00 | 5.00 | 0.05 | Air | not pinned — inert unless mode = 2 |
| 51 | `KickVerticalMps` | 1.60 | m/s | 0.00 | **6.00** | 0.05 | Air | **max hard** — inert unless mode = 2 |
| 52 | `KickMinSpeedMps` | 4.05 | m/s | 0.00 | 12.00 | 0.05 | Air | not pinned — inert unless mode = 2 |

**No blank cells, no `TBD`.** Twenty-two rows.

**Recommended first experiments** (MOVE-4a's practice: a named experiment, never a moved
default) — `ChainBonusMps` → **0.60**; `AirJumpMode` → **1**, then **2**;
`DuckWalkSpeedFraction` → **0.30** if the duck should cost something;
`TouchdownSlideImmediate` → **1** to feel the fluid jump-into-slide.

### 11.2 Which defaults are live on arrival, and why it is not a blanket answer

MOVE-4's discipline is that a new term ships at its exact no-op. Applied per row rather than
across the board:

- **The crouch family (31–41) ships live.** None of it can fire without a deliberate held button
  through a landing, so a player who does not make that input sees a bit-identical body.
- **`ChainBonusMps` ships at 0.00 — the exact no-op — because the chain is the one thing here
  that fires with no new input at all.** It would change the behaviour of the ordinary hop chains
  MOVE-3a §8.8 built a course for, which is exactly the baseline the lab exists to compare
  against. It is a slider drag away.
- **`AirJumpMode` ships at 0** for the same reason, plus a stronger one: a double jump changes
  what every gap distance in the playground means.

### 11.3 The bounds that are structural, not taste

`MotorTuning.Validate` must clamp these, and `MotorTuningInvariants` must report the coupled
ones — a hand-edited JSON file must not get past them.

- **`SlideExitSpeedMps < SlideEnterSpeedFraction × MoveSpeed`, hard.** The gap *is* the
  anti-chatter mechanism, the same law the skid's pair already carries. Collapse it and the slide
  re-enters the tick it exits — MECHANICS §2's flicker case, produced by a slider.
- **`SlideMinSec < SlideMaxSec`, hard.** Otherwise the two exits overlap and §3.4's "cannot
  overlap" row becomes a lie.
- **`SlideDeceleration` min 0.5, hard.** At 0 the speed exit never fires and only `SlideMaxSec`
  ends the slide; at 0 *with* `SlideMaxSec` at its max that is a 3-second unsteerable state
  reached by two sliders neither of which is wrong alone. This is exactly the coupled shape
  MOVE-4a §3.4 built the live invariant readout for.
- **`ChainMaxDepth` max 7 and `AirJumpCountMax` max 3, hard, and for a different reason from
  every other bound in this table: they are *wire widths*.** 3 bits and 2 bits. A slider past
  them does not produce a bad feel, it produces a truncated field and a peer whose chain depth
  disagrees with the server's. These two must be clamped in the validator, not merely in the
  widget.
- **`KickConversionFraction` max 1.00, hard.** Above 1 the Kick returns more speed than the fall
  carried — energy from nothing, and precisely the floaty read the variant exists to avoid.
- **`KickVerticalMps` max 6.00.** At 8.4 it would equal `JumpVelocity` and mode 2 would become
  mode 1 wearing mode 2's name. Two modes that do the same thing is worse than one.
- **`DuckWalkSpeedFraction` max 1.00.** Above a jog the duck walk stops being a duck walk. Note
  that this bound is *taste*, not safety: no tuning can create a DuckWalk → Slide cycle, because
  §3.3 has no such row — the safety is structural.

### 11.4 A standing rule for MOVE-5b's tests, because 22 of 30 existing knobs are pinned

MOVE-4a §3.3 found 22 of 30 knobs pinned by some test, 8 of them to within ±0.1%, and the packet
warns that every value added here will acquire a pin. **A test written against these rows may
assert a *property*; it must not assert a *feel literal*.**

- **Properties that should be pinned** (and the rows they bind): the anti-chatter gap holds
  (33/34); every state has an exit under every legal tuning (35/38); `ChainDepth` never exceeds
  its wire width (45); `AirJumpsUsed` never exceeds its (47); the exact-no-op identity at
  `ChainBonusMps = 0` and `AirJumpMode = 0` (44/46) — that one is the MOVE-4-style identity guard
  and it is the most valuable test in the wave.
- **Rows that must stay free to move** (a test asserting a literal here has decided a feel
  question that is Talon's): 31, 36, 39, 41, 42, 43, 48, 50, 52, and `SlideDeceleration`'s value
  within its bounds.

### 11.5 A shape finding for the panel — four of these knobs are discrete

`MotorKnob` carries a `float` per row and MOVE-4's table has no boolean or enum column. Knobs 32,
40, 45, 46 and 47 are discrete, and they ride as floats with `Step = 1` and an integer range,
which is correct and needs no change to `MotorTuning`. **But a continuous slider with two or
three stops reads as broken.** This is a finding for MOVE-5b and the panel — a discrete widget
for `Step == 1` rows with an integer range — not a change to the tuning shape.

---

## 12. The animation contract — the chain's only readable channel

No UI, per §6.6. The chain is read entirely off the body. Everything below is a contract for the
pose layer; **the wire cost is zero**, because `ChainDepth` and `Verb` are already replicated and
the pose reads them exactly as it reads `SkidRemaining` today.

**Honest scope note:** this repo has no authored-animation system for the player — the body is
procedural (`AvatarVisual`, `LocomotionProfile`, `GreyboxAvatarBody`). These are therefore
descriptions of *pose deltas a procedural rig can apply*, ordered by how load-bearing each is.

### 12.1 Per chain depth

| Depth | What changes | Channel | Readable from |
|---|---|---|---|
| **0** | nothing — the baseline run and jump | — | — |
| **1** | in-air: arms sweep back, torso pitches forward ~8° | pose | close range; **this is the actor's own confirmation that a chain started**, not a partner's |
| **2** | pitch deepens to ~15°, **trailing leg extends** into a long-jump stride — a straight silhouette line from trailing toe to leading hand | **silhouette** | **across a field.** The first depth that changes the outline rather than the shading, so a partner 30 m away in the dark sees a diagonal where there was an upright |
| **3** | adds a **ground-contact tell**: a scuff/dust burst at the feet on each touchdown, and the takeoff kick fires at full drive | ground event | from behind, which is where a partner usually is. Scales the existing landing channel (`StepEvents.FallSpeed`, `TakeoffKickSec`) rather than adding a system |
| **4** | adds a short low-opacity **trail** from the trailing foot (~0.25 s of history), and the 15° torso pitch **holds through the landing** instead of recovering | motion + grounded pose | any range. The held grounded pose is the one that matters: during the 0.35 s grace it tells a partner "he is still hot", which is the only moment a partner could act on it |

**How a partner reads it, in priority order:** silhouette (depth 2) → motion (depth 4) → ground
event (depth 3). Ordered deliberately so that the channel which survives distance and darkness
carries the load and the fine channels only add — INTERACTION §8.2's redundant-channel rule,
satisfied with no UI element.

**What must ship for the verb to be readable at all: depth 2's silhouette change.** Depths 1, 3
and 4 are additive. Stated so an implementer under time pressure cuts the right things.

### 12.2 The verb poses

| State | Pose | Why it is distinguishable |
|---|---|---|
| **Slide** | tucked ball, low, facing travel | Already distinct from anything shipped. |
| **Tuck** | **braced** — compressed, arms in, weight low and still | The button is being *held*. MECHANICS §2.5. |
| **DuckWalk** | **settled** — extended, head lowered, a walking gait | The button has been *released*. |

Tuck and DuckWalk share their locomotion exactly (§5.1), so §2.5's "distinguishable from each
other" is a real requirement here. Braced versus settled is the same difference the input has,
which is the cheapest legibility available.

### 12.3 The transitions themselves

INTERACTION §2: feedback at the exact moment of the trigger. Every verb entry and every verb exit
gets a pose change on the tick it happens — in particular **the pop-up on release must be visible
on the tick the button comes up**, because that is the input whose responsiveness the whole spec
is built around.

### 12.4 The Kick

Legs snap down and back, torso pikes forward, arms drive back — **the body looks like it pushed
against something.** That is the entire visual argument for why the Kick is not a floaty double
jump, and if the pose does not sell it, the mechanic will not either. Clip-safe under CANON §0.8:
slapstick, no gore, nothing taken.

---

## 13. Open questions — ripe, not resolved

Each names its trigger, its options and their tradeoffs, per `DESIGN-BIBLE.md`'s ripeness law.

**O1 — The ground slide always has a jump in front of it. Is that the feel you want?**
*(§2, §8.3.)* Under one zero-latency button, a grounded press always jumps, so "sprint, hold,
slide" is really "sprint, hold, jump, land, slide". **Trigger:** Talon plays the
`TouchdownSlideImmediate` knob once. **Options:** (a) accept it — the jump-into-slide is arguably
a better verb than a flat slide, and it costs nothing; (b) a second binding for a standalone
slide — **one free bit in `buttons2`, no length change, no extra protocol bump**, and it makes
the skid→slide carve reachable; (c) a tap/hold split on the jump button — **refused on sight**,
it costs frames. **Tradeoff:** (b) contradicts ruling 1's one-button reading, which is why it is
a question and not a decision.

**O2 — Standing up without jumping.** *(§5.4.)* The duck walk's only exit is a press edge, which
also fires a jump; a duck-walker who wants to stand takes a 1.02 m hop. **Trigger:** the first
time a duck walk is used somewhere a hop is loud, visible or dangerous — i.e. the first stealth
or low-ceiling context, which does not exist yet. **Options:** accept the hop; the same second
binding as O1; a stand-on-release-while-stationary rule (cheap, but it makes the latch
conditional and is a §3.5 flicker risk worth analysing before adopting).

**O3 — §9's momentum grant amends MOVE-3a §2.3.** *(§9.)* It is required — the chain and the Kick
cannot exist without it — and it is an exact no-op at the shipped defaults. It is listed here
rather than made silently because §2.3 is an approved, argued clause and a wave should not amend
one by inference. **Trigger:** MOVE-5b, before writing the air block. **Tradeoff:** none found;
the amendment strengthens §2.3's stated guarantee rather than weakening it.

**O4 — The composed ceiling is 15.58 m/s and an ~11.5 m jump.** *(§7.2.)* Chain 4 plus a Kick is
1.80 × sprint. That is a real change to what the playground's geometry means and to what any
future level must be built against. **Trigger:** Talon switching `AirJumpMode` to 2 with
`ChainBonusMps` above zero. **Options:** accept; cap `ChainMaxDepth` lower (2 gives 13.9 m/s);
gate the Kick behind `ChainDepth == 0` (a rule the player must be told, which §6.5 argues against).

**O5 — THRILL has not been applied and must be before these verbs leave the lab.** A duck walk in
the dark, a chain jump's noise, and a slide's inability to stop are all dread-relevant, and the
noise channel INTERACTION §9.3 describes **has no implementation in this repo at all**. **Trigger:**
the first packet that puts any of these verbs in a night scene. **Not resolvable here:** THRILL is
applied through `/direct`, never read inline, and a lab is not its subject.

---

## 14. Bible check

```
Bibles applied:  MECHANICS (governing — this is a state machine, boundaries, races,
                 idempotency) and INTERACTION (every verb is a thing the player does
                 directly, and the weight principle is an INTERACTION constraint).
                 THRILL: does not apply to a lab — noted in O5 that it will apply the
                 moment these verbs reach the game, via /direct, never read inline.
                 LEVEL: does not apply — the playground is a dev harness and MOVE-3a §8's
                 declaration of that stands. BEHAVIOR: does not apply — no autonomous
                 entity. DESIGN: §13's forks are stated ripe per its ripeness law.

Items checked:   MECHANICS §1 (boundary conditions) — every threshold's inclusive/exclusive
                 sense stated: slide entry inclusive >=, slide exit inclusive <=, chain
                 grace exclusive at 0, hold window inclusive at 12 ticks, and the entry
                 boundary lands in a defined state on BOTH sides (Slide above, Tuck below)
                 so no input produces nothing. §2 (state transition integrity) — laws V0/V1/V2,
                 §3.2's four-part spec for all six states, and §3.5's flicker table with
                 minimum round-trip times for every pair; the three no-cycle results are
                 structural (no such row in §3.3), not numeric, so no tuning can create them.
                 §2.5 (perceivability) — Tuck and DuckWalk share locomotion exactly and are
                 given opposite poses, braced vs settled (§12.2). §3 (simultaneous events) —
                 §3.4's fixed six-step order plus the seven-case precedence table, each with
                 a stated winner and reason. §4 (idempotency) — a jump edge cannot
                 double-increment the chain (a grounded tick must intervene); AirJumpsUsed is
                 a counter, not a bool, for the same reason. §6 (numeric rules, extremes) —
                 zero, max and coupled extremes checked on every one of the 22 new knobs;
                 §11.3's structural bounds are the result, including the two WIRE-WIDTH
                 bounds that must be clamped in the validator rather than the widget.
                 §7: n/a, no win/loss condition.

                 INTERACTION §2 (feedback at the moment) — §12.3, and specifically the pop-up
                 on release. §4 (range and timing pinned, tap vs hold) — the tap is the jump,
                 the hold is the verb, the window is 0.20 s / 12 ticks, all stated not
                 inherited. §6 (reversibility) — every state declares whether it is held or
                 latched and what resets it. §7 (interrupt handling) — control lock, water,
                 leaving the floor and ballistic all resolve to Normal; §3.2 shows no state a
                 player cannot leave. §8.1/§8.2 (consequence scope and redundant channels) —
                 §12.1's silhouette-first ordering, actor-scope at depth 1 and group-scope
                 from depth 2. §9.5 (every verb declares its feedback) — §12.
                 THE WEIGHT PRINCIPLE, as an INTERACTION constraint: no state in §3.2 costs a
                 frame of input responsiveness; there is no recovery lockout anywhere in the
                 table; SlideMinSec blocks only the SELF-termination and never the release
                 exit; the deferred-launch charged jump was REJECTED in §4.1 precisely because
                 it would have cost 3-6 ticks on a tap; and the duck walk's jump-out is
                 immediate (§5.4) for the same reason.

                 CANON §0.8 (the clip test) — the tuck, the slide and the Kick are slapstick
                 poses; no gore, nothing taken, every beat survives an out-of-context clip.
                 §0.9 (the rehearsal law) — not engaged: these are traversal verbs used
                 identically by day and by night by construction, not day activities needing
                 a night twin.

Result:          PASS, with two things fixed on the way and one finding raised.
                 FIXED 1: the water guard (§4.7). A wading player holding jump satisfies the
                 crouch trigger exactly, because WaterGeometry.JumpAllowed denies the jump and
                 the hold window then accrues — the body would have tucked in the shallows.
                 T3 forbids every verb unless Water == Dry. This was found by the INTERACTION
                 §7 interrupt sweep, not by the design.
                 FIXED 2: the Kick's compounding runaway (§7.2). The naive
                 `flatSpeed += gain` grows speed without bound at up to +4.54 m/s per flight
                 against a 0.15 m/s per-tick ground cost. Replaced by a `max(flatSpeed,
                 sprintWish + gain)` top-up, found by MECHANICS §6's extremes check.
                 FINDING: §9's momentum grant amends MOVE-3a §2.3 and is required — raised as
                 O3 rather than made silently, per the packet's out-of-scope rule.
```

---

## 15. What an implementer reads first

1. `docs/design/2026-08-26-airborne-control-and-jump-shape.md` — MOVE-3a. §2.3 (the clause §9
   amends), §6.1 (`LandingSpeedMultiplier`, which the chain jump depends on) and §6.2 (the
   no-auto-bounce rule, which is what makes the ground hold window reachable at all).
2. `docs/design/2026-08-26-movement-tuning-surface.md` — MOVE-4a. §2.2's table shape, §2.3's
   structural-bound reasoning, §2.5 on why knob 19 is a fraction, §3.4 on coupled invariants.
3. `scripts/net/AvatarMotor.cs` — `Step`'s ordering, `StepJump`, `StepSkid`, `ShouldEnterSkid`,
   `GravityFor`. §3.4's six-step order must land inside `Step` in that order.
4. `scripts/net/MoveState.cs` — `SkidRemaining`'s doc comment is the precedent §10.1 argues
   from and §10.3 argues against.
5. `scripts/net/NetCodec.cs` — the flags byte at `span[5]` (full), `buttons2` (one bit used,
   seven free), `SnapshotBytes` and `InputEntryBytes`.
