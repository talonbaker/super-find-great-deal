# The movement tuning surface — MOVE-4a

**Date:** 2026-08-26
**Packet:** MOVE-4a (systems-design)
**Branch:** `feat/2026-08-26-move-4-knobs`
**Implements:** MOVE-4b (the `MotorTuning` seam), MOVE-4c (the apex hang + the camera dip),
MOVE-4d (the knob panel), MOVE-4e (headed re-measurement)
**Supersedes nothing.** Extends `docs/design/2026-08-26-airborne-control-and-jump-shape.md`
(MOVE-3a), which stays authoritative for everything it decided.

---

## 0. The one-paragraph version

Thirty movement feel values become live-tunable through one `MotorTuning` object, with a
slider each, a range wide enough to reach the edge of wrong, and a pinned/not-pinned verdict
that names the test standing behind it. Two new terms join them: an **apex-hang gravity
attenuation** that extends `GravityFor`'s pure three-way branch without adding a latch, and a
**landing camera dip** driven off the landing definition that already exists. **Every default
in this document is today's shipped value, and both new terms default to their exact no-op**
— because Talon played the MOVE-3 jump on 2026-08-26 and said "I love it! It's great!", and a
spec that silently moves the arc he approved has thrown away the only baseline the lab has.
The tuning is saved as JSON and printed back as pasteable C#; the round trip is the point of
the wave.

---

## 1. What this spec is, and what it is not

**Is:** the contents of the tuning object, the range and step of every slider, the audit of
which values a test pins, the two new terms' arithmetic, the save/load/print format, and the
guard that stops ambient tuning state from silently desyncing a networked session.

**Is not:** a retune. **Not one default in this document differs from what is on the branch
today.** Where a value would benefit from moving, this spec says so as a *recommended first
experiment* with its own arithmetic, and leaves the default alone.

**Also not:** the panel's visual layout (MOVE-4d owns it); the plumbing of the
const-to-property seam (MOVE-4b owns it — §9 tells it what it must not break); the
`AvatarVisual` scale-squash toggle (decided in code, `AvatarVisual.cs` ~line 480, and this
spec does not reopen it); crouch-slide, duck walk, chain jump, double jump, dive, roll,
mantle or long jump (wave MOVE-5); networked co-op testing of the lab.

**Arithmetic vs. feel, stated once and honoured throughout.** Following MOVE-3a's practice:
every number in §4 marked **arithmetic** is derived and shown, and reproduces the measured
baseline to three decimals. Every claim about how something *reads* is marked
**UNVERIFIED — reasoned, not measured** and stays unverified until Talon plays it.

---

## 2. The knob table

### 2.1 The discrete model every number in this document uses

`AvatarMotor` integrates semi-implicit Euler at a fixed `TickDelta = 1/60`, and every figure
below is computed on that discrete clock, not on continuous math. The exact sequence, read
off `Step`:

- **The jump tick.** `prev.Grounded` is true, so the gravity branch is skipped entirely.
  `velocity.Y` is set to `JumpVelocity`, `MoveAndSlide` advances the body by `v·dt`.
  `MovementPlayground` records `_takeoffPos` on the first tick `IsOnFloor()` returns false —
  which is this tick, *after* the move. **The free 0.14 m of that first tick is therefore
  inside `_takeoffPos` and is not part of the measured apex.**
- **Each airborne tick *n*.** `g = GravityFor(v_{n-1}, …)` — the gravity is chosen from the
  velocity at the *start* of the tick; then `v_n = v_{n-1} − g·dt`; then the body advances by
  `v_n·dt`.
- **Apex** = `max(y) − takeoffPos.Y` = `Σ v_k·dt` over the rising ticks.
- **Airtime** = clock delta from the takeoff tick to the tick `IsOnFloor()` returns true
  again, which is **one tick after** the tick whose height crosses zero (the floor contact is
  resolved by `MoveAndSlide` and read back on the following tick).

**The model reproduces both measured baselines exactly**, which is why the rest of this
document can be trusted to three decimals:

| | model | MOVE-3d measured |
|---|---|---|
| held sprint jump, apex | 22 rising ticks, `Σv·dt` = **1.533889 m** | **1.534 m** |
| held sprint jump, airtime | 43 ticks = **0.716667 s** | **0.717 s** |
| jog tap, apex | 7 rising ticks, `Σv·dt` = **0.466667 m** | **0.467 m** |
| jog tap, airtime | 19 ticks = **0.316667 s** | **0.317 s** |

Worked, held sprint: `v_0 = 8.4`, `Δv = 22/60 = 0.366667` per tick, so `v_n = 8.4 − 0.366667n`
and the last positive velocity is `v_22 = 0.333333`.
`Σ_{k=1..22} v_k = 22×8.4 − 0.366667×253 = 184.8 − 92.766667 = 92.033333`; `÷60 = 1.533889`.
Worked, jog tap (released on the first airborne tick, so `g = 66`, `Δv = 1.1`):
`v_7 = 8.4 − 7.7 = 0.7` is the last positive; `Σ_{k=1..7} v_k = 58.8 − 30.8 = 28.0`;
`÷60 = 0.466667`. **The "jog tap" in MOVE-3d's capture is an immediate release** — that is
what makes it land exactly on the minimum-hop arithmetic.

*(MOVE-3a §3.3's 1.60 m / 0.67 m are continuous-math figures on a different launch
convention — they include the free launch tick. They are not wrong; they are a different
measurement. Where the two disagree, the discrete figures above are the ones MOVE-4e will
re-measure, so this spec uses those.)*

### 2.2 The table

Thirty rows. `AvatarMotor` unless the owning class says otherwise. **Current value = the
default**, without exception. Groups: **Ground, Gravity, Jump, Air, Skid, Landing, Camera**
(the packet's suggested cut, kept — see §2.5 for the one change considered and rejected).

| # | Constant | Owner | Current / default | Unit | Min | Max | Step | Group |
|---|---|---|---|---|---|---|---|---|
| 1 | `MoveSpeed` | `AvatarMotor` | 5.4 | m/s | 1.0 | 12.0 | 0.1 | Ground |
| 2 | `SprintMultiplier` | `AvatarMotor` | 1.6 | × | 1.00 | 3.00 | 0.05 | Ground |
| 3 | `Acceleration` | `AvatarMotor` | 9 | m/s² | 1.0 | 40.0 | 0.5 | Ground |
| 4 | `Deceleration` | `AvatarMotor` | 21 | m/s² | 2.0 | 60.0 | 0.5 | Ground |
| 5 | `TurnAcceleration` | `AvatarMotor` | 34 | m/s² | 2.0 | 80.0 | 1.0 | Ground |
| 6 | `TurnLerp` | `AvatarMotor` | 12 | s⁻¹ | 1.0 | 45.0 | 0.5 | Ground |
| 7 | `AimTurnLerp` | `AvatarMotor` | 22 | s⁻¹ | 1.0 | 60.0 | 0.5 | Ground |
| 8 | `Gravity` | `AvatarMotor` | 22 | m/s² | 5.0 | 45.0 | 0.5 | Gravity |
| 9 | `FallGravityMultiplier` | `AvatarMotor` | 1.35 | × | 0.50 | 3.00 | 0.05 | Gravity |
| 10 | `ApexHangStrength` | `AvatarMotor` **(new)** | **0.00** | fraction | 0.00 | 0.90 | 0.01 | Gravity |
| 11 | `ApexHangWindowMps` | `AvatarMotor` **(new)** | 2.00 | m/s | 0.25 | 6.00 | 0.05 | Gravity |
| 12 | `JumpVelocity` | `AvatarMotor` | 8.4 | m/s | 2.0 | 16.0 | 0.1 | Jump |
| 13 | `JumpReleaseGravityMultiplier` | `AvatarMotor` | 3.00 | × | 1.00 | 8.00 | 0.05 | Jump |
| 14 | `CoyoteTimeSec` | `AvatarMotor` | 0.12 | s | 0.00 | 0.40 | 0.01 | Jump |
| 15 | `JumpBufferSec` | `AvatarMotor` | 0.12 | s | 0.00 | 0.40 | 0.01 | Jump |
| 16 | `AirControlBuild` | `AvatarMotor` | 0.45 | fraction | 0.00 | 1.00 | 0.01 | Air |
| 17 | `AirControlTurn` | `AvatarMotor` | 0.35 | fraction | 0.00 | 1.00 | 0.01 | Air |
| 18 | `AirControlBrake` | `AvatarMotor` | 0.30 | fraction | 0.00 | 1.00 | 0.01 | Air |
| 19 | `SkidEnterSpeedFraction` | `AvatarMotor` **(reshaped)** | 0.75 | × `MoveSpeed` | 0.20 | 1.60 | 0.01 | Skid |
| 20 | `SkidAlignmentMax` | `AvatarMotor` | −0.50 | dot | −1.00 | 0.00 | 0.01 | Skid |
| 21 | `SkidDeceleration` | `AvatarMotor` | 13 | m/s² | 2.0 | 40.0 | 0.5 | Skid |
| 22 | `SkidExitSpeedMps` | `AvatarMotor` | 1.20 | m/s | 0.10 | 4.00 | 0.05 | Skid |
| 23 | `SkidMaxSec` | `AvatarMotor` | 0.75 | s | 0.00 | 2.00 | 0.05 | Skid |
| 24 | `LandMinFallMps` | `AvatarVisual` | 2.5 | m/s | 0.5 | 10.0 | 0.1 | Landing |
| 25 | `LandFullFallMps` | `AvatarVisual` | 14 | m/s | 3.0 | 30.0 | 0.5 | Landing |
| 26 | `TakeoffKickSec` | `AvatarVisual` | 0.16 | s | 0.02 | 0.60 | 0.01 | Landing |
| 27 | `TakeoffKickMinDriveFraction` | `AvatarVisual` | 0.40 | fraction | 0.00 | 1.00 | 0.01 | Landing |
| 28 | `CameraDipStrengthM` | `SandboxCamera` **(new)** | **0.00** | m | 0.00 | 0.50 | 0.01 | Camera |
| 29 | `CameraDipAttackSec` | `SandboxCamera` **(new)** | 0.05 | s | 0.01 | 0.30 | 0.01 | Camera |
| 30 | `CameraDipRecoverSec` | `SandboxCamera` **(new)** | 0.26 | s | 0.02 | 1.20 | 0.01 | Camera |

No blank cells and no `TBD`. Rows 26 and 27 are `private const` in `AvatarVisual` today and
must be widened to `public` by MOVE-4b; rows 28–30 do not exist yet at all.

### 2.3 Range rationale — why these bounds and not others

Ranges are set to satisfy three things at once: **reach the edge of wrong** (a slider that
cannot produce a bad value cannot teach anything), **never produce a divide-by-zero, a
negative duration or a NaN**, and **never produce a state a player cannot leave**.

**The bounds that are structural, not taste** — MOVE-4b must clamp these in the tuning
validator, not merely in the slider widget, so a hand-edited JSON file cannot get past them:

- **`ApexHangStrength` max 0.90, hard.** At exactly 1.0 with the weight at full (`v_y = 0`)
  the effective gravity is **zero**: velocity stops changing, position stops changing, and the
  body hangs at its apex forever. That is a state with no exit — MECHANICS §2 — reached by a
  slider, not by a bug. 0.90 leaves 10% of gravity at the worst point and the apex is always
  crossed. *(MECHANICS §6: the extreme was checked before shipping the formula, not after.)*
- **`ApexHangWindowMps` min 0.25, hard.** It is the divisor in `u = |v_y| / window`. Zero is a
  divide-by-zero on every airborne tick.
- **`JumpReleaseGravityMultiplier` min 1.00, hard.** Below 1.0 the released branch becomes
  *weaker* than the held branch, so letting go of the jump key would make you go **higher**.
  That inverts the variable-jump model and destroys MOVE-3a §3.2's third argument (energy can
  never be gained by mashing the key, so apex is bounded at the held-throughout value). At
  exactly 1.00 the cut is a clean no-op and the jump becomes fixed-height, which is a
  legitimate and instructive setting.
- **`Gravity` min 5.0, hard.** At or near zero a body that leaves the ground never returns —
  another exitless state, and the `_maxAirFallMps` landing gate never fires. 5.0 is
  ridiculous-moon territory (the edge of wrong) while still bringing the body down inside a
  couple of seconds.
- **`LandFullFallMps` min 3.0 and `> LandMinFallMps + 0.5`, hard.** It is the divisor in
  `fall / LandFullFallMps`; and if it ever fell *below* `LandMinFallMps` every qualifying
  landing would clamp to full intensity, collapsing the two-constant gate into one.
- **`TakeoffKickSec` min 0.02, `CameraDipAttackSec` min 0.01, `CameraDipRecoverSec` min 0.02
  — all hard.** Each is a divisor in a normalised envelope.
- **`SkidExitSpeedMps < SkidEnterSpeedFraction × MoveSpeed`, hard.** The gap between entry and
  exit *is* the anti-chatter mechanism (`StepSkid`'s doc comment). Collapse it and the skid
  re-enters the tick it exits — MECHANICS §2's flicker case, produced by a slider.
- **`TurnLerp` max 45, and a code fix it forces.** `ResolveYaw` applies `TurnLerp * dt`
  **unclamped**, while the `AimTurnLerp` path right above it is wrapped in `Mathf.Min(1f, …)`.
  At 60 Hz any `TurnLerp` above 60 gives a lerp factor above 1 and the facing overshoots and
  oscillates. **This is a live defect in shipped code that the slider merely exposes** — see
  §9.3. The max of 45 (factor 0.75 at 60 Hz) holds even if MOVE-4b does not fix it.
- **`SkidAlignmentMax` max 0.00.** Above zero the "reversal" test would fire on an input
  *aligned* with travel, i.e. running forward would skid.

**The bounds that are taste, chosen to bracket the shipped value by roughly 3–4× in the
direction that teaches:** rows 1–7, 12, 21–23, 24–27. Each is roughly `default ÷ 3` to
`default × 2.5`, rounded to a readable number. **Ambiguity about a value is mine to settle
(the packet's own rule); these are settled, not deferred.**

**Steps** are set at roughly 1–2% of each range, so a slider drag is perceptible without
being coarse, and so a printed C# value is short.

### 2.4 Four constants deliberately excluded — and two of them are the interesting ones

`AvatarMotor` has 26 `public const`s. Twenty-one are knobs above. The remaining five:

| Constant | Why it is not a knob |
|---|---|
| `TickRate`, `TickDelta` | Aliases of `NetProfile`. Not feel — the simulation rate. Changing either changes the protocol and every arithmetic figure in this document; it is a wave of its own, not a slider. |
| `MinSpeedFactor` (0.55) | An **anti-cheat clamp** on a client-reported value, not a feel value. Its doc comment says so. A slider on an anti-cheat bound is a slider on an attack surface. *(Independently frozen to exactly 0.55 by `NightPressureTests.cs:347` and ceilinged at 0.62 by `ToolStanceTests.cs:354`.)* |
| `LandingSpeedMultiplier` (1.0) | **A slider here would be a lying knob.** Its own doc comment: *"It is referenced, not applied. There is no multiply in `Step`, because the correct implementation of '1.0' is the absence of a line."* Moving the slider would change nothing, and a knob that does nothing is worse than no knob — it teaches a false lesson about the game. *(And it is frozen at exactly 1.0 by `AirborneControlTests.ALanding_PreservesHorizontalSpeedExactly` — `tests/unit/AirborneControlTests.cs:470` — so the "no multiply" rule has a guard.)* If a landing tax is ever wanted it is a MOVE-5 design question with MOVE-3a §6.1's argument to overturn first, not a slider. |
| `AirWishSpeedFloorMps` (= `MoveSpeed`) | **The second lying knob.** `Step` deliberately reads the *scaled* ground wish (`MoveSpeed × speedFactor × waterMul`), **not** this constant — see its own doc comment. The constant exists so the value has a name in the file. A slider on it would move nothing. |

**Finding for MOVE-4d:** both lying knobs must be **absent from the panel**, not present and
disabled. A greyed-out slider reads as "not yet wired"; an absent one reads as "not a thing".

### 2.5 One reshaped row, and one group cut considered and rejected

**Row 19, `SkidEnterSpeedMps` → `SkidEnterSpeedFraction`.** Today it is
`MoveSpeed * 0.75f` — a *derived* constant, and the derivation is load-bearing: the whole
SKID-1 argument is that the threshold sits between the walk gear's top speed and the jog's,
both of which are themselves fractions of `MoveSpeed`. Shipping it as an absolute m/s field
would silently decouple it, so moving the `MoveSpeed` slider would leave the skid threshold
stranded at an absolute number and quietly change which gears skid. **It ships as the
fraction, with the panel printing the live effective m/s beside the slider** (`0.75 ×
5.4 = 4.05 m/s`). The printed C# emits the original derived form (§6.4).

**The group cut.** A separate **Facing** group for `TurnLerp` / `AimTurnLerp` was considered
and rejected: two rows do not earn a group header, and both are things the body does while
moving on the ground. They stay in **Ground**, last.

---

## 3. The pinned-constant audit

### 3.1 What "pinned" means here, and why the obvious reading is wrong

The obvious reading — *"a test asserts this literal, so moving the slider turns the suite
red"* — **is false, and getting it wrong is the trap this item exists to catch.**

Every pinning assertion in the repo runs against **compile-time constants**. `MotorTuning` is
a *runtime* object. So moving a slider **does not turn the suite red**. It does something
worse: it silently puts the running game outside an invariant a test believes it is still
inside, and the test stays green while it happens. The red only arrives later, when Talon
prints the tuning back as C# (§6.4) and pastes it into `AvatarMotor.cs` — at which point a
value he has been happily playing with for an hour fails the suite for a reason nothing in
the lab ever mentioned.

**So "pinned" in this document means: this knob has a real invariant behind it, the invariant
is enforced somewhere other than the running game, and the panel is the only place that can
warn you before the paste.**

**One subtlety MOVE-4b must not trip over.** After the const-to-property conversion (§9), a
test that reads `AvatarMotor.MoveSpeed` symbolically stops comparing two compile-time constants
and starts comparing `MotorTuning.Current` against a literal. **That is still green under
`dotnet test`, and provably so: nothing in the test process ever calls `TryApply`, so `Current`
is `Default` for the whole run, and `Default` is the shipped literal (§8).** But it means the
suite's verdict now depends on a static field's initial value, so **`MotorTuning.Current` must
be initialised to `Default` at static-construction time and never lazily from the JSON file** —
loading the lab's saved tuning is the *playground scene's* explicit startup call (§6.3), never a
static initialiser. A lazy load would make the xUnit suite's result depend on whether a
developer had once dragged a slider on that machine, which is the worst failure mode in this
entire document.

### 3.2 The method, and its positive control

**Method.** For each row: (1) grep `tests/unit/**`, `tests/scenes/**`, `scripts/**/*SelfTest.cs`
(the C# behind the `tests/Run-*.ps1` scene suites) and `tests/**/*.ps1` for the constant name;
(2) for every hit, decide whether the test *reads* the constant symbolically (it follows the
value — NOT pinned) or *bounds* it (a literal, an inequality, or an ordering — PINNED);
(3) where it is a bound, derive the numeric floor or ceiling.

**No test was run.** The packet forbids it and the machine has one GPU that Talon may be
using. Every verdict below is from reading the assertion.

**Positive control, required before any "not pinned" verdict is trusted.** The method must
find the one pin that is known in advance, and it does:

> `tests/unit/EelProfileTests.cs:159` —
> `Invariant1_BodyComesToRestBeforeControlReturns_AtEveryCharge`
> asserts `EelProfile.TotalFlightSec < EelProfile.RagdollSec` (1.2 s), where
> `EelProfile.SkidSec = LaunchSpeedFullMps / AvatarMotor.Deceleration` = `9.0 / 21` = 0.428571 s,
> giving a floor of `9.0 / (1.2 − 0.710435)` = **18.384 m/s²** — the packet's stated ~18.4,
> reproduced.

**And the control pays for itself twice.** First, the same assertion pins three more constants
the packet did not know about. `EelProfile.RiseSec`, `PeakHeightM` and `FallSec` are all
derived from `AvatarMotor.Gravity`, `JumpVelocity` and `FallGravityMultiplier`, and they are
the other half of `TotalFlightSec`:

```
FlightSec = RiseSec + FallSec
          = Jv/G  +  Jv / (G·√F)            [F = FallGravityMultiplier]
          = (Jv/G) · (1 + 1/√F)
          = 0.381818 × 1.860663 = 0.710437 s

TotalFlightSec = 0.710437 + 0.428571 = 1.139008 s  <  1.200 s   ✓ margin 0.060992 s
```

Solving that one inequality for each term in turn gives four one-sided bounds:
`Deceleration ≥ 18.384`, `Gravity ≥ 20.261`, `JumpVelocity ≤ 9.121`,
`FallGravityMultiplier ≥ 0.960`. *(0.771429 s = `1.2 − SkidSec` at `Deceleration = 21`; the
four are coupled — moving two at once tightens both.)*

**Second, and this is the finding that changes the shape of the whole item: `Invariant1` is not
the tightest pin on any of the four.** The exhaustive sweep found strictly tighter, *two-sided*
windows on every one of them:

| Knob | `Invariant1` says | The **tightest** pin says | Tightened by |
|---|---|---|---|
| `Deceleration` | ≥ 18.384 | **[20.892, 21.080]** | `AirborneControlTests.cs:217` |
| `Gravity` | ≥ 20.261 | **[21.01, 25.31]** | `AirborneControlTests.cs:317-322` |
| `JumpVelocity` | ≤ 9.121 | **[7.805, 8.590]** | `AirborneControlTests.cs:317-322` |
| `FallGravityMultiplier` | ≥ 0.960 | **[1.076, 2.594]** | `AirborneControlTests.cs:317-322` |

**`Gravity`'s real floor is 21.01 against a shipped 22 — 0.05% of downward margin, not the 7.9%
`Invariant1` alone implies — and `Deceleration`'s entire legal window is 0.19 m/s² wide.** Had
this spec stopped at the pin it was handed, MOVE-4d would have shipped labels that were wrong
by two orders of magnitude on the two knobs a designer reaches for first. **That is the whole
value of the positive control: it proved the method, and then the method found that the seed
was the loosest bound in the file rather than the tightest.**

### 3.2b Twenty-three of thirty are pinned, and eight are frozen

| Knob | Frozen by | Window |
|---|---|---|
| `MoveSpeed` | `NightPressureTests.Constants_MatchTheirSourcesOfTruth` — `tests/unit/NightPressureTests.cs:346` | **exactly `5.4f`** — `Assert.Equal` with **no tolerance**, against the hand-typed literal at `NightPressureGuarantee.cs:96` |
| `SprintMultiplier` | `ToolStanceTests.MeasuredReadiedSpeeds` — `tests/unit/ToolStanceTests.cs:375` | [1.59981, 1.60019] |
| `TurnAcceleration` | `LocomotionTests.Skid_DidNotRetuneOrdinarySteering` — `tests/unit/LocomotionTests.cs:757` | 34 ± 0.0001 |
| `CoyoteTimeSec` | `AirborneControlTests.CoyoteTime_FiresAtTheWindowEdge_AndNotOneTickLater` — `tests/unit/AirborneControlTests.cs:383` | 0.12 ± 0.00001 |
| `AirControlBuild` | `AirborneControlTests` `[InlineData]` `:78-79`, asserted `:93` | [0.449, 0.451] |
| `AirControlTurn` | same, `:80-81` | [0.349, 0.351] |
| `AirControlBrake` | same, `:77`, plus `InRange(0.30, 0.60)` at `:62` and the ordering `Brake < Turn < Build` at `:64`/`:66` | [0.300, 0.301] |
| `LandingSpeedMultiplier` *(not a knob — §2.4)* | `AirborneControlTests.ALanding_PreservesHorizontalSpeedExactly` — `:470` | exactly 1.0 |

**This is a fact about the suite, not about the movement, and it is the most important thing
MOVE-4 learns.** A large part of the movement test suite asserts *the current value*, not a
property. That is defensible — it catches accidental drift, which is real and has bitten this
repo — but it means **every deliberate retune is a multi-file edit, and nothing inside the lab
can tell you so.** Everything in §3.4 and §6.4 follows from it. §11.6 raises it as the design
question it is; this spec does not resolve it.

### 3.3 The verdict table

Verdicts are per-row and cover all thirty, from the exhaustive sweep. **Bounds are the
tightest found, not the first found** — every one is derived in the sweep and reproduced here
with its source. Where a knob has several pins, the binding one is named and the others are
noted.

| # | Knob | Verdict | Binding pin (test method — file:line) | Legal window |
|---|---|---|---|---|
| 1 | `MoveSpeed` | **PINNED — frozen** | `NightPressureTests.Constants_MatchTheirSourcesOfTruth` — `tests/unit/NightPressureTests.cs:346` (`Assert.Equal`, no tolerance) | **exactly 5.4** |
| 2 | `SprintMultiplier` | **PINNED — frozen** | `ToolStanceTests.MeasuredReadiedSpeeds` — `tests/unit/ToolStanceTests.cs:375` | [1.59981, 1.60019] |
| 3 | `Acceleration` | **PINNED** | `LocomotionTests.SprintRamp_IsLongEnoughToSee_AndBrakingIsFaster` — `tests/unit/LocomotionTests.cs:387`, narrowed by `:388` | [7.855, 15.75] |
| 4 | `Deceleration` | **PINNED** | `AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold` — `tests/unit/AirborneControlTests.cs:217` | [20.892, 21.080] |
| 5 | `TurnAcceleration` | **PINNED — frozen** | `LocomotionTests.Skid_DidNotRetuneOrdinarySteering` — `tests/unit/LocomotionTests.cs:757` | 34 ± 0.0001 |
| 6 | `TurnLerp` | **not pinned** | — (see §9.3: an unclamped lerp — a code defect, not a test pin) | — |
| 7 | `AimTurnLerp` | **PINNED** | `LocomotionTests.Facing_TracksTravel_UnlessAnAimIsHandedIn` — `tests/unit/LocomotionTests.cs:481` (floor) and `:474` (ceiling) | (6.514, 45) |
| 8 | `Gravity` | **PINNED** | `AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately` — `tests/unit/AirborneControlTests.cs:317-322` | [21.01, 25.31] |
| 9 | `FallGravityMultiplier` | **PINNED** | same — `AirborneControlTests.cs:317-322` | [1.076, 2.594] |
| 10 | `ApexHangStrength` | **PINNED — before it is written** | same — `AirborneControlTests.cs:317-322`, whose `SimulateJump` (`:531`) calls `AvatarMotor.GravityFor` directly | joint with row 11 — §3.7 |
| 11 | `ApexHangWindowMps` | **PINNED** | same — §3.7 | joint with row 10 — §3.7 |
| 12 | `JumpVelocity` | **PINNED** | same — `AirborneControlTests.cs:317-322`; also an exact identity, `EelProfileTests.LaunchVertical_IsExactlyAJump…` — `EelProfileTests.cs:130` | [7.805, 8.590] |
| 13 | `JumpReleaseGravityMultiplier` | **PINNED** | same — `AirborneControlTests.cs:318`/`:320`/`:322` | [2.40, 4.60] |
| 14 | `CoyoteTimeSec` | **PINNED — frozen** | `AirborneControlTests.CoyoteTime_FiresAtTheWindowEdge_AndNotOneTickLater` — `tests/unit/AirborneControlTests.cs:383` | 0.12 ± 0.00001 |
| 15 | `JumpBufferSec` | **PINNED — floor only** | `AirborneControlTests.BufferedJump_PressedBeforeLanding_FiresOnTheLandingTick` — `tests/unit/AirborneControlTests.cs:407-430` (`> 6 ticks`) | > 0.100 s |
| 16 | `AirControlBuild` | **PINNED — frozen** | `AirborneControlTests` `[InlineData]` `:78-79`, asserted `:93` | [0.449, 0.451] |
| 17 | `AirControlTurn` | **PINNED — frozen** | same — `:80-81` | [0.349, 0.351] |
| 18 | `AirControlBrake` | **PINNED — frozen** | same — `:77`; plus `InRange(0.30, 0.60)` `:62` and the ordering `Brake < Turn < Build` `:64`/`:66` | [0.300, 0.301] |
| 19 | `SkidEnterSpeedFraction` | **PINNED (two-sided)** | ceiling `AirborneControlTests.cs:218`; floor `LocomotionTests.Skid_EntersOnlyOnACommittedReversal` — `LocomotionTests.cs:599` | effective m/s ∈ (2.43, 4.473] |
| 20 | `SkidAlignmentMax` | **PINNED (geometry)** | `LocomotionTests.Skid_EntersOnlyOnACommittedReversal` — `:608` (90° must not skid) and `:612` (WASD diagonal must) | [−0.70711, 0) |
| 21 | `SkidDeceleration` | **PINNED** | `LocomotionTests.Skid_AtASprint_SlidesFarEnoughToWatch` — `:569`/`:571`; floor `Skid_HasFourExits_AndNoneOfThemCanBeRefused` — `:680` | (9.92, 16.53) |
| 22 | `SkidExitSpeedMps` | **PINNED — ceiling only** | `LocomotionTests.Skid_CannotChatter` — `tests/unit/LocomotionTests.cs:691` (`exit < enter × 0.5`) | < 2.025 |
| 23 | `SkidMaxSec` | **PINNED — floor only** | `LocomotionTests.Skid_HasFourExits_AndNoneOfThemCanBeRefused` — `:680`; weaker floor `NetCodecRoundTripTests.cs:171`/`:194` via `NetCodec.cs:322`'s clamp | > 0.5833 s |
| 24 | `LandMinFallMps` | **not pinned** | — **zero references anywhere under `tests/`** | — |
| 25 | `LandFullFallMps` | **not pinned** | — same | hard min 3.0 + ordering, §2.3 |
| 26 | `TakeoffKickSec` | **not pinned** | — | hard min 0.02 |
| 27 | `TakeoffKickMinDriveFraction` | **not pinned** | — | — |
| 28 | `CameraDipStrengthM` | **not pinned** (new) | — | comfort labelling, §5.5 |
| 29 | `CameraDipAttackSec` | **not pinned** (new) | — | hard min 0.01 |
| 30 | `CameraDipRecoverSec` | **not pinned** (new) | — | hard min 0.02 |

**Twenty-three pinned, seven not.** The seven clean knobs are 6, 24, 25, 26, 27, 28, 29, 30
minus the two brand-new gravity terms that turned out to be pinned anyway — i.e. **`TurnLerp`,
the two landing gates, the two takeoff-kick values, and the three camera-dip values.**
Everything a designer would reach for first is pinned; everything unpinned is either new or
cosmetic. That is not a coincidence and it is not a criticism of the suite — it is what a suite
looks like when it was written to protect a signed-off feel.

**Secondary pins worth knowing, all looser than the binding one above but all real:**
`Deceleration` also by `AirborneControlTests.cs:143` ([20.33, 21.26]), `LocomotionTests.cs:655`
(literal `> 18.4`), `LocomotionTests.cs:402` (a restatement of Eel Invariant 1) and
`EelProfileTests.cs:190` Invariant 3 ([18.599, 36.615]). `MoveSpeed` also by
`ClipTimeWarpTests.TheNominalSpeedsMatchTheGameSpeedsCloselyEnoughToBeAnchors` —
`tests/unit/ClipTimeWarpTests.cs:117-119` ((5.3261, 5.4337)) and `:106` (< 5.7154), and softly
by `tests/Run-CheatTest.ps1:89` (`$MaxLegitSpeed = 7.0`, enforced at `:234`). `SprintMultiplier`
also by `LocomotionTests.cs:789` ([1.2357, 2.2833]), `:586` (> 1.3966) and
`ToolStanceTests.cs:129` (> 1.1739). `SkidDeceleration` also by `LocomotionTests.cs:654`
(literal `< 18.4`) and `:575` (> 9.30).

**Three "obvious" pins that turn out not to exist, checked and negative** — each was searched
for specifically and is **not** asserted anywhere:

- **`MaxBodyTilt` is NOT pinned.** `SandboxSelfTest.phys_fall_mesh_clamped`
  (`scripts/game/sandbox/SandboxSelfTest.cs:449`, condition at `:426`) asserts
  `|BodyTiltX| <= MaxBodyTilt + 0.02` — but `AvatarVisual.cs:3748` clamps that same quantity to
  `±MaxBodyTilt`, so the assertion is **self-referential and holds for any value**. It is still
  correctly excluded from the knob table (it is a floor-clamp safety bound, not feel), but the
  reason is design, not a test.
- **`LandMinFallMps` / `LandFullFallMps` are NOT pinned.** Zero references under `tests/`.
  `SandboxSelfTest`'s 6 m drop lands at ~17 m/s, which saturates the intensity clamp at 1.0, so
  moving either constant leaves it green.
- **`ToolStance`'s run hysteresis is NOT pinned.** `RunEnterSpeedMps` and `RunExitSpeedMps` are
  both `MoveSpeed` multiples (`× 1.08`, `× 1.0`), so the ordering is structurally true for any
  positive `MoveSpeed` and `MoveSpeed` cancels out of
  `ToolStanceTests.RunWalkSplitIsHystereticAndNeverFlickers` (`:136`) entirely. The ordering is
  still worth preserving — §2.5's fraction argument does it structurally — but no test guards
  it.
- **`AirWishSpeedFloorMps` is NOT pinned**, confirming §2.4's exclusion from a second
  direction: the symbol is never referenced by any test, because `Step` reads the scaled
  `groundWish` instead.

### 3.4 What the panel does about a pinned knob — **warn, do not lock. Argued.**

**Decision: pinned knobs are movable, with a persistent warning, and with the invariant
evaluated live.** Not locked.

**The argument for warning, and the audit makes it decisive rather than merely reasonable:**

1. **Locking would leave a panel with seven working sliders out of thirty.** Twenty-three of
   the thirty rows are pinned (§3.3), and **eight are frozen to within ±0.1%**. `MoveSpeed` is
   frozen to *exactly* 5.4 by an untoleranced `Assert.Equal`. Lock the pinned rows and the
   Ground, Gravity, Jump, Air and Skid groups are read-outs; the only thing left to drag is a
   landing gate, a takeoff kick and a camera dip. **That is not a movement feel lab.** The
   packet's own range rule — a slider that cannot produce a bad value cannot teach anything —
   would be satisfied by seven sliders and violated by twenty-three.
2. **Most of these bounds are snapshots of the current value, not properties of the game.** A
   `[InlineData(…, 0.45f)]` asserting `AirControlBuild` is 0.45 is not an invariant about air
   control; it is a record that 0.45 is what MOVE-3b shipped. Locking a slider against a
   *snapshot* freezes the game at the value the lab exists to question.
3. **Where a bound *is* a property, it is a consequence somebody may want to pay.**
   `Deceleration`'s Eel floor exists because `EelProfile.SkidSec` derives from it and a blast
   launch must settle inside the 1.2 s ragdoll — and the eel kit ships **behind `EelKitFlag`,
   off**. A movement feel Talon prefers may well be worth re-deriving `EelProfile` for. That is
   a design conversation the lab should *start*, not a door it should lock.
4. **A lock states the wrong thing.** "You may not move this" is false. The true statement is
   "moving this outside [20.892, 21.080] costs you
   `AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold`, and here is
   by how much." The second is actionable; the first is not.

**Against locking, one honest concession:** the failure is *deferred* — the suite goes red at
the paste, not at the drag. That is precisely why the warning must be loud and must travel
**with the value**, not merely appear at the slider.

**So, three places, all required (MOVE-4d):**

- **On the slider.** A pin badge with the binding test's method name and the window, always
  visible — not a hover tooltip. E.g. `PINNED [20.892, 21.080] — AirborneControlTests.
  MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold`. **The slider track paints the legal
  window as a band**, so "how far can I move this before it costs me" is answered by looking,
  not by reading.
- **A live invariant readout, per pin cluster.** The panel evaluates the actual inequalities
  from the current knob values, continuously — `EelProfile invariant 1: 1.139 / 1.200 s — OK`,
  `JumpApex window: apex 1.674 (1.45–1.75), airtime 0.733 (0.650–0.770) — OK` — turning red and
  naming the breach when a combination fails. **Strictly better than a per-slider bound, because
  the bounds are coupled**: the `Gravity` / `JumpVelocity` / `FallGravityMultiplier` /
  `ApexHangStrength` window is one four-dimensional constraint, and lowering two of them a
  little each breaches an inequality neither breaches alone. No per-slider number can say that.
- **In the printed C#.** Every breached pin emits a banner and a per-line tag naming the test
  that will fail and by how much (§6.4). The paste carries its own warning into the file.

### 3.5 Rows 1–2 — the `MoveSpeed` / `SprintMultiplier` cluster, and the dome red

`MoveSpeed` and `SprintMultiplier` fan out further than any other knob. Confirmed compile-time
derivations (§9.1): `LocomotionProfile.WalkSpeedMps` / `JogSpeedMps` / `SprintSpeedMps`,
`ToolStance.RunEnterSpeedMps` / `RunExitSpeedMps`, `AvatarMotor.SkidEnterSpeedMps`,
`AvatarMotor.AirWishSpeedFloorMps`, plus the lab constants in `GreyboxPlayerLab`.

**The binding pin is not the fan-out; it is a hand-typed literal in a mirror.**
`NightPressureGuarantee.WalkSpeedMps` is the literal `5.4f` (`NightPressureGuarantee.cs:96`),
and `NightPressureTests.Constants_MatchTheirSourcesOfTruth` (`tests/unit/NightPressureTests.cs:346`)
asserts `Assert.Equal(AvatarMotor.MoveSpeed, NightPressureGuarantee.WalkSpeedMps)` — **exact
float equality, no tolerance.** Any change to `MoveSpeed`, however small, turns it red. The
same test freezes `MinSpeedFactor` at `:347` the same way. Both are **currently green**; the
mirrors were updated by CATCH-1.

**The gear ordering is real but structural, not asserted.** `LocomotionProfile`'s thresholds
are all `MoveSpeed`-relative (`JogEnterMps = WalkSpeedMps × 1.12`, `SprintEnterMps = JogSpeedMps
× 1.22`), so they *follow* `MoveSpeed` and the hysteresis tests at `LocomotionTests.cs:340-363`
are **not** `MoveSpeed` pins. `ToolStance`'s run hysteresis is the same shape. **The ordering is
preserved automatically as long as every derived value stays a fraction of `MoveSpeed`, which is
row 19's whole argument** (§2.5) — and nothing guards it if a future change breaks that pattern.

**Correction to a widely-quoted belief, and it matters because two roles have repeated it.**
`AvatarMotor.cs:64-67` says the dome's escapability contract is measured against
`CycleBands.PlayerSprintSpeedMps` and "does NOT survive this speed". Both halves need care:

- **`CycleBands.PlayerSprintSpeedMps` (`CycleBands.cs:170`) is the bare literal `8.64f` and is
  NOT asserted against `AvatarMotor` anywhere.** The comment at `NightPressureTests.cs:345`
  claiming `NightCycleSelfTest` keeps that pairing is **false** — `NightCycleSelfTest.cs`
  mentions `AvatarMotor` only in stale prose at `:198`. **So the dome does not pin `MoveSpeed`
  or `SprintMultiplier` at all**, and MOVE-4d must not label it as if it did.
- **The dome red is real and is in the scene suite, not the xUnit suite.**
  `NightCycleSelfTest.FrontOutrunsASprintingPlayerOnEveryDay`
  (`scripts/game/world/NightCycleSelfTest.cs:203-246`, driven by `tests/Run-NightCycleTest.ps1`)
  fails **four separate checks** at the shipped values: `:207` (front 5.333 m/s vs sprint
  8.64 → ratio 0.617, required 1.05–1.15), `:216` (a sprinter from 155 m *does* make it home),
  `:239` and `:242` (`SafeReturnRadiusM` 259.2 m against a scan saturating at 160 m). `:219` and
  `:225` still pass. **This is Talon's open question, deliberately left red** — the panel must
  say so, or the first person to move the `MoveSpeed` slider will believe they broke it.
  *(`tests/Run-NightCycleTest.ps1:23` still quotes the superseded 4.86 m/s.)*

### 3.6 Rows 4 and 19 — the momentum-keep rule, which turned out to be a real test

MOVE-3a §2.4 proves that the most `AirControlBrake` can shed over the longest possible jump is
`0.30 × 21 × 0.710 = 4.473 m/s`, and states the result as a rule:

> "any run committed enough to skid on the ground is committed enough that it cannot be
> cancelled in the air. One threshold, two systems."

**This spec expected that to be a prose-only property. It is not — it is asserted.**
`AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold`
(`tests/unit/AirborneControlTests.cs:217-218`) checks both halves:
`Near(4.47f, Deceleration × AirControlBrake × 0.710, 0.02f)` at `:217`, and
`shed > SkidEnterSpeedMps` at `:218`.

Two consequences:

- **`Deceleration`'s binding window is [20.892, 21.080]** — 0.19 m/s² wide, from the `±0.02`
  tolerance on `:217`. This is the tightest non-frozen pin in the table and it is nine times
  tighter than the Eel floor the packet named.
- **`SkidEnterSpeedFraction`'s effective speed has a ceiling of 4.473 m/s** from `:218`, against
  a shipped 4.05 — **0.42 m/s of headroom, 10%.** The gap MOVE-3a called "one threshold, two
  systems" is a 10% margin, not a comfortable one, and moving `MoveSpeed` (frozen anyway) or the
  fraction upward eats it quickly.

The panel shows this as a rule-shaped readout beside the numbers:
`air-brake ceiling 4.473 m/s vs skid entry 4.050 m/s — rule holds (10% margin)`.

### 3.7 Rows 10–11 — the apex hang is pinned before it is written

**`AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately` does not
assert against literals — it runs its own discrete 60 Hz Euler simulation**, `SimulateJump` at
`tests/unit/AirborneControlTests.cs:531`, and that simulation calls `AvatarMotor.GravityFor`
directly:

```csharp
vy -= AvatarMotor.GravityFor(vy, held, locked: false, ballistic: false) * Dt;
```

**So the moment `GravityFor` reads `ApexHangStrength` from `MotorTuning.Current`, the apex hang
is inside a test that was written before it existed.** That is a good thing — it means the new
term cannot be shipped with a default that breaks the approved arc without the suite saying so —
and it puts a hard, computable ceiling on the term that nobody would have guessed.

`SimulateJump` includes the launch tick, so its baseline figures differ from MOVE-3d's by that
0.14 m: **apexHeld 1.6739 m, airtimeHeld 0.7333 s, apexTap 0.6978 m, airtimeTap 0.350 s,
ratio 2.399.** The assertions are `Near(1.60, apexHeld, 0.15)`, `Near(0.67, apexTap, 0.15)`,
`Near(0.710, airtimeHeld, 0.06)`, `Near(0.357, airtimeTap, 0.06)` and `apexHeld/apexTap ≥ 2.0`.

**The binding one is the held airtime**, whose ceiling is `0.710 + 0.06 = 0.770 s` against a
baseline of 0.7333 — **a budget of 0.0367 s.** Using §4.4's closed form for the airtime the hang
adds:

```
Δairtime  =  W · (1/G_rise + 1/G_fall) · ( 1/(1 − S/2) − 1 )
          =  W · 0.079125 · k                                   [at shipped G and F]

budget:      W · k  ≤  0.0367 / 0.079125  =  0.4638 m/s
```

| `ApexHangWindowMps` | Test-asserted ceiling on `ApexHangStrength` |
|---|---|
| 1.00 | 0.646 (the hard 0.90 cap binds first at ≥ 1.42) |
| **2.00** | **0.377** |
| 3.00 | 0.268 |
| 4.00 | 0.207 |
| 6.00 | 0.141 |

**§4.4's recommended first experiment (0.35 at window 2.00) sits just inside that ceiling, with
about 7% of margin.** The apex side is not binding — `apexHeld` has 0.076 m of headroom to
1.75 m and the hang adds 0.009 m. **MOVE-4d must render this as a live curve on the two Gravity
sliders**, not as a static number, because the ceiling moves with the window and with `Gravity`
and `FallGravityMultiplier` themselves.

**None of this makes the term unshippable, and none of it changes the default.** At
`ApexHangStrength = 0` the early return fires and `GravityFor` is bit-identical, so
`JumpApex_BracketsTheSpecRange` is byte-for-byte green on `MotorTuning.Default`. The pin is a
**paste-time** pin like every other one in §3.3.

---

## 4. The apex hang

### 4.1 The shape — a common attenuation, not a fourth branch

`GravityFor` keeps its exact three-way shape and gains a multiplicative attenuation applied to
**whichever branch was selected**:

```csharp
public static float GravityFor(float velocityY, bool jumpHeld, bool locked, bool ballistic)
{
    float g;
    if (velocityY < 0f)                                           g = Gravity * FallGravityMultiplier;
    else if (velocityY > 0f && !jumpHeld && !locked && !ballistic) g = Gravity * JumpReleaseGravityMultiplier;
    else                                                          g = Gravity;

    // The apex hang. A common attenuation of whichever branch applied, never a fourth branch.
    if (locked || ballistic || ApexHangStrength <= 0f) return g;
    float u = Mathf.Min(1f, Mathf.Abs(velocityY) / ApexHangWindowMps);   // window >= 0.25, clamped
    float w = 1f - (u * u * (3f - 2f * u));                              // 1 - smoothstep(u)
    return g * (1f - ApexHangStrength * w);
}
```

Two new fields, both in the Gravity group:

| Name | Default | Unit | Range | Meaning |
|---|---|---|---|---|
| `ApexHangStrength` | **0.00** | fraction | 0.00 – 0.90 | Share of gravity removed at exactly `v_y = 0` |
| `ApexHangWindowMps` | 2.00 | m/s | 0.25 – 6.00 | The `|v_y|` at which the term has fully decayed |

### 4.2 Why this form and no other — five properties, each load-bearing

1. **It is a gravity term, not a latched state.** MOVE-3a decision 3 forbids "simplifying" a
   gravity multiplier into a per-tick velocity multiply, because `g·dt` is frame-rate
   independent and a per-tick multiply is not. That argument binds here identically and this
   form obeys it: the return value is still an acceleration, still consumed as `v -= g·dt`.
   **MOVE-4c must not turn this into a velocity multiply, a timer, or a `MoveState` field.**
2. **No new state, so nothing to enter, leave or chatter (MECHANICS §2).** The attenuation is a
   pure function of `|v_y|` and two scalars. There is no "in the hang" flag. `v_y` crosses each
   window edge once per jump, monotonically, so even a threshold-shaped reading of it could not
   flicker — and there is no threshold to flicker.
3. **No new wire byte.** The snapshot layout is untouched. `MoveState` is untouched. The input
   buttons byte — which MOVE-3b already filled at protocol v12 — is untouched.
4. **Continuous, and continuously differentiable.** `w = 1 − smoothstep(u)` has `w(0) = 1`,
   `w(1) = 0`, `w'(0) = 0` and `w'(1) = 0`. So gravity is C¹ at the window edge, and the kink
   that `|v_y|` has at zero is cancelled by `w'(0) = 0`. **A linear ramp `w = 1 − u` would
   have been continuous in `g` but not in `dg/dv_y`, giving a jerk step at the window edge —
   the "hitch rather than a float" the packet warns against.** The smoothstep costs two
   multiplies and removes it.
   **One honest statement:** the shipped motor *already* has an acceleration discontinuity at
   `v_y = 0` — rising uses 22 and falling uses 29.7. This term does not remove it and does not
   add to it: the **same** factor multiplies both sides at `v_y = 0`, so the existing 22 → 29.7
   step is scaled, never reshaped. **No new discontinuity is introduced. That is the property
   the constraint asks for; removing the pre-existing one is not this packet's job** and would
   change the jump Talon approved.
5. **It never reverses the branch ordering.** At any given `v_y` the same factor multiplies all
   three branches, so `g_released > g_held` always holds for `ApexHangStrength < 1`. MOVE-3a
   §3.2's third argument survives verbatim: energy is never gained by mashing the key, and the
   apex stays bounded at the held-throughout value. **This is why 0.90 is the max and 1.00 is
   not** — at 1.00 with `w = 1` the factor is zero, all three branches collapse to zero
   gravity, and the ordering argument is not merely weakened but undefined.

**Why the term is gated off for `locked` and `ballistic`.** Same reason MOVE-3a gave those
guards: a control-locked or blast-launched body must get **exactly** the gravity it gets
today, bit for bit. It is also what keeps `EelProfile`'s whole arithmetic true — `RiseSec`,
`PeakHeightM` and `FallSec` are computed from bare `Gravity` and would silently stop
describing the blast arc otherwise, which would break §3.2's invariant from the other side.

### 4.3 The no-op setting, stated explicitly

> **`ApexHangStrength = 0.00` is the exact no-op.** The early return fires before any
> arithmetic, so `GravityFor` returns bit-identical values to today's for every input. The
> held sprint jump measures **apex 1.534 m / airtime 0.717 s** and the jog tap measures
> **apex 0.467 m / airtime 0.317 s** — the MOVE-3d values, to three decimals, unchanged.
> **`ApexHangWindowMps` has no effect at all while strength is zero**, so its default of 2.00
> is a starting position for the slider, not a behaviour.

**That is the default, and the reason is Talon's own sentence.** He played the MOVE-3 jump and
said it was great. The brief's "lighter rise, a brief hang, a stronger fall" is the *reason the
term exists*; it is not a mandate to ship it on, and the packet is explicit that a default which
silently changes the approved jump is a spec that loses the baseline. **The term ships built,
wired, ranged and off, one slider drag from being felt.**

### 4.4 The recommended first experiment, with arithmetic

**`ApexHangStrength = 0.35`, `ApexHangWindowMps = 2.00`.** Computed on the discrete model of
§2.1, tick by tick:

| | baseline (0.00) | at 0.35 / 2.00 | change |
|---|---|---|---|
| held sprint, apex | 1.5339 m | **1.5427 m** | **+0.0088 m (+0.57%)** |
| held sprint, airtime | 0.7167 s (43 ticks) | **0.7500 s (45 ticks)** | **+0.0333 s (+4.7%)** |
| jog tap, apex | 0.4667 m | **0.4668 m** | +0.0002 m (+0.04%) |
| jog tap, airtime | 0.3167 s (19 ticks) | **0.3333 s (20 ticks)** | +0.0167 s (+5.3%) |

**That asymmetry — airtime up ~5%, apex up half a percent — is the whole point of the term.**
A hang is *time at the top*, not height. The horizontal reach of every jump grows by the same
~5% as the airtime and nothing else about the arc changes, so §8's ledge-bank and gap-bank
clearances in MOVE-3a move by centimetres, not by a re-plan.

**A closed form for the airtime change**, derived and then checked against the tick-by-tick
result, so MOVE-4d can label a slider without simulating:

```
Δairtime  ≈  W · (1/G_rise + 1/G_fall) · ( 1/(1 − S/2) − 1 )

  W = ApexHangWindowMps, S = ApexHangStrength,
  G_rise = Gravity, G_fall = Gravity × FallGravityMultiplier,
  and S/2 is the mean of (1 − smoothstep) over the window, which is exactly 1/2.

At S=0.35, W=2:   2 × (1/22 + 1/29.7) × (1/0.825 − 1)
                = 2 × 0.237374/3 … = 0.033569 s      vs. the tick-by-tick +0.033333 s   ✓
```

**Range endpoints, so the slider's reach is stated rather than discovered:**

| Setting | Δairtime (closed form) | held-sprint airtime | held-sprint apex | inside the test window (§3.7)? |
|---|---|---|---|---|
| 0.00 / any | 0 | 0.717 s | 1.534 m | yes — the exact no-op |
| 0.35 / 2.00 *(recommended first)* | +0.034 s | 0.750 s | 1.543 m | **yes, with ~7% of margin** |
| 0.70 / 3.00 | +0.128 s | ≈ 0.845 s | **1.601 m** (tick-by-tick) | **no** — ceiling at this window is 0.268 |
| 0.90 / 6.00 *(the edge of wrong)* | +0.388 s | ≈ 1.105 s | ≈ 2.1–2.2 m *(analytic)* | **no** — ceiling at this window is 0.141 |

The 0.90 / 6.00 row is a moon jump — 54% more airtime and 40% more height. **It is meant to be
reachable and meant to be obviously wrong.** Rows 1–2 are arithmetic on the tick model; row 3's
apex is tick-by-tick and its airtime is the closed form; row 4 is a continuous-math integral
(`h = ∫₀^{v₀} v/g(v) dv`) and is labelled as an estimate — **MOVE-4e measures it headed.**

**Rows 3 and 4 breach `JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately`, and that
is exactly as designed.** The slider must reach past the window or the window is the slider.
§3.7 gives the ceiling as a function of the window so the panel can draw it live, and §6.4's
print names the test in the paste.

**UNVERIFIED — reasoned, not measured:** that 0.35 / 2.00 is *perceptible*. A 33 ms change in a
717 ms arc is 2 frames at 60 Hz. It may read as exactly the float the brief asks for, or it may
read as nothing at all, in which case 0.55 / 2.5 is the next stop. **Nobody knows until Talon
plays it, and that is what the slider is for.**

### 4.5 The interaction with `JumpReleaseGravityMultiplier` — when the windows overlap

**They do overlap, on every tapped jump, and the resolution is defined rather than incidental.**

The release cut fires while `v_y > 0` and the key is up; the hang fires while `|v_y| <
ApexHangWindowMps`. A tap released early therefore spends its last rising ticks in **both**,
and the answer is that the hang multiplies the cut:

```
g = Gravity × JumpReleaseGravityMultiplier × (1 − ApexHangStrength · w)
```

**Three consequences, all wanted, all stated:**

1. **The cut still cuts.** At the recommended setting the harshest overlap is
   `66 × (1 − 0.35) = 42.9 m/s²`, still comfortably above the held branch's attenuated
   `22 × 0.65 = 14.3`. Releasing the key always ends the ascent sooner than holding it.
   **The ordering `g_released > g_held` is preserved at every `v_y` because the same factor
   multiplies both** (§4.2 property 5) — this is not a coincidence of the current numbers, it
   is structural, and it holds across the whole of both sliders' ranges.
2. **A short hop gets *proportionally* more hang than a long one.** A tap's `|v_y|` is inside
   the window for a larger share of its (much shorter) arc. The jog tap's airtime gains 5.3%
   against the held sprint's 4.7% — the small jump floats slightly more, in relative terms.
   That is the right sign: a tap is the arc with the least character to begin with.
3. **`GravityFor` is still a pure function, and here is the exact claim.** It is pure in
   *(velocityY, jumpHeld, locked, ballistic, `MotorTuning.Current`)*. It reads no clock, no
   node, no `MoveState` field it was not handed, and it stores nothing between calls. There is
   no latch and no timer. The tuning object it reads is **ambient**, which is a real weakening
   of the old "pure in its arguments" claim — and it is exactly the weakening §7's
   prediction-parity guard exists to make safe: `MotorTuning.Current` is immutable for the
   whole lifetime of any session in which two machines simulate. **Under that guarantee,
   determinism across server, owner prediction and reconciliation replay is unchanged**, which
   is the property MOVE-3a §5 rests on.

---

## 5. The landing camera dip

### 5.1 The open question, answered: **threshold-gated.** Not every landing.

The brief left it open. The answer is threshold-gated, at the **existing** `LandMinFallMps`
gate, and the argument is three independent reasons, each sufficient:

1. **Firing on every landing would *be* a second definition of "landing", which is the exact
   defect the packet forbids.** `AvatarVisual.LandMinFallMps`'s doc comment records that the
   pose and the sound were once two copies of the same literal and were unified by JUMP-1
   precisely so they could not disagree. A camera that dips on a landing the body does not
   absorb is the third copy, disagreeing on its first day. There is one definition of a
   landing in this game and the camera uses it.
2. **The nausea case is not the big landing, it is the hop chain.** MOVE-3a §6.3 makes hop
   chains the substrate for emergent tricks and §6.1 keeps `LandingSpeedMultiplier` at 1.0
   specifically so a chain does not decay. A dip on every touchdown puts a periodic vertical
   **viewpoint** oscillation at hop cadence — roughly 2–4 Hz, which is the band motion
   sickness lives in. A body-relative squash at that cadence is fine because the player is
   *watching* it; a camera translation at that cadence is what makes people put the controller
   down. The threshold is what keeps the chain smooth: hops in a chain land well under
   2.5 m/s, so a chain produces no dip at all.
3. **Stairs.** MOVE-3a §8.9 puts stairs in the calibration course. Descending them produces a
   touchdown per step at a fall speed far below 2.5 m/s. An every-landing dip makes walking
   down stairs unwatchable, and would have been discovered by Talon rather than by this spec.

**The concession, stated:** a 2.4 m/s landing gets no dip while a 2.6 m/s landing gets one at
the `LandMinIntensity` floor. That is a visible step at the threshold — and it is the *same*
step the knee absorb already has, at the *same* fall speed, for the *same* reason. One
threshold, three channels (pose, sound, camera), all stepping together. A camera that faded in
smoothly while the knees snapped in would be worse, not better.

### 5.2 It reads its intensity from the existing gate, and adds nothing

```
if (fall < AvatarVisual.LandMinFallMps)  ->  no dip at all
intensity = Mathf.Clamp(fall / AvatarVisual.LandFullFallMps,
                        AvatarVisual.LandMinIntensity,   // 0.25
                        1f);
```

**Byte-identical to `AvatarVisual`'s own absorb intensity**, including the 0.25 floor, and for
`LandMinIntensity`'s own stated reason: *"a landing that qualified must be visible, or the gate
reads as a bug on the frames just past it."* The camera has the same problem and takes the same
answer. `LandMinIntensity` is **not** a knob — it is the gate's own legibility floor, and a
slider on it would let the panel author the exact bug the constant exists to prevent.

**A discrepancy found while writing this, reported not resolved.** There are *already* two
intensity curves off the one gate: `AvatarVisual.cs:3341` clamps to `[LandMinIntensity, 1]`,
while `SandboxAvatar.cs:2562` — the `ActorEvent.Land` fan-out — clamps to `[0, 1]`. Same gate,
same fall speed, two intensities. **The dip follows `AvatarVisual`'s**, because the packet
names `AvatarVisual`'s constants and because the pose is the channel the dip is visually paired
with. Unifying the two is not this packet's scope; it is logged in Open questions.

### 5.3 Where it is applied — composing with the sphere sweep, not fighting it

**The dip lowers the camera's focus height, and nothing else.** In
`SandboxCamera._PhysicsProcess`, one line, inserted where `focusHeight` is read:

```csharp
float focusHeight = FocusHeight - CurrentDipM();          // <- the whole change
focusHeight = Mathf.Max(focusHeight, MinFocusHeightM);    // 0.25 m, a non-knob safety floor
Vector3 targetFocus = basePos + Vector3.Up * focusHeight;
```

Everything downstream then works unchanged and unaware:

- `ClampFocusToFreeSpace(...)` runs on the dipped focus, so the cast origin is still pulled
  into free space.
- `ArmLimitForPitch(_pitch, focusHeight, _speedArm)` **must be passed the dipped
  `focusHeight`**, not the undipped one. It is the analytic bound that keeps the lens
  `LensGroundClearanceM` above the focus plane at high look angles; handed a stale height it
  would authorise an arm the dipped focus cannot afford. *(This is the one line MOVE-4c is
  most likely to get wrong.)*
- The `SpringArm3D`'s 0.25 m sphere cast is the real safety net and is untouched. **The dip
  cannot put the camera inside geometry, because the dip never places the camera — it only
  moves the point the arm orbits, and the arm still sweeps.**
- The `_focus.Lerp(targetFocus, …)` follow smoothing runs on top, so even a step in the dip
  envelope arrives at the eye softened by `followStiffness`.

**Two orderings that matter:**

- **The swim override wins outright.** `targetFocus.Y` is overwritten with an absolute
  waterline plane when swimming; the dip is applied to `focusHeight` *before* that, so the
  waterline rule is unaffected. A swimmer does not land, so there is nothing to lose.
- **`BodyHideBufferM`'s own-body cull reads `_renderedArm`, which is downstream of everything
  above**, so a deep dip that shortens the arm still drops the avatar's mesh correctly. No
  change needed.

**"Weight is skin, not friction" — the dip costs zero frames of input responsiveness, and the
reason is structural rather than careful:** it lives entirely in `SandboxCamera`, which is
client-local presentation. It touches no `MoveIntent`, no `MoveState`, no `AvatarMotor` call and
nothing that crosses the wire. It cannot delay, gate or damp an input, because it never sees
one. It is not replicated and each client computes its own from the fall speed its own body
reported.

### 5.4 The envelope — attack, hold nothing, recover

An **impulse with a linear attack and a linear release**, not a spring:

```
t = seconds since the qualifying touchdown
dip(t) = strength · intensity ·  t / CameraDipAttackSec                     for t <  A
       = strength · intensity · (1 − (t − A) / CameraDipRecoverSec)         for A <= t < A + R
       = 0                                                                  otherwise
```

- **Linear, not a spring.** A spring overshoots on the way back and lifts the camera *above*
  its rest height — which reads as a bounce, not a landing, and would be a second visual event
  nobody asked for. `AvatarVisual`'s own absorb decays linearly for the same stated reason
  ("the recovery IS the settle"). One event, one shape.
- **`CameraDipAttackSec = 0.05` (3 ticks).** Short enough to read as impact, long enough not to
  be a one-frame cut. A one-frame drop is a jump cut, not a thump.
- **`CameraDipRecoverSec = 0.26`, deliberately the same number as `AvatarVisual.LandAbsorbSec`.**
  The knees and the camera are recovering from the same event and should finish together.
  **They are separate knobs anyway** — MOVE-4c must not derive one from the other — because the
  brief is explicit that the dip is "a separate, independent tunable knob… not tied to the
  squash or gravity curve." The *default* agreeing is the design; the *coupling* is not.
- **Re-trigger is idempotent (MECHANICS §4):** a second qualifying landing inside an active
  envelope **restarts** it at the higher of (the new intensity, the currently displayed dip),
  never sums. Summing is how two landings 40 ms apart produce a dip twice as deep as any single
  landing can, which is the "fires twice" case the bible names.

### 5.5 Strength, range, and where it becomes nauseating

| | value |
|---|---|
| **default** | **0.00 m — the exact no-op** |
| range | 0.00 – 0.50 m |
| step | 0.01 m |
| **recommended first experiment** | **0.12 m** |

**The default is 0.00 and the reason is the packet's own rule.** `SandboxCamera` has no dip of
any kind today. "Today's shipped value" for this knob is therefore zero, and item 7's
default-identity contract requires `MotorTuning.Default` to reproduce today's behaviour
exactly. **The dip ships built, wired and off**, one slider drag from being felt — the same
treatment the apex hang gets, for the same reason.

**Where it becomes nauseating — named so the range does not quietly include it:**

| Band | Reading |
|---|---|
| 0.00 – 0.15 m | Reads as weight. 0.12 m at full intensity over a 0.05 s attack is 2.4 m/s of focus travel — a thump. |
| 0.15 – 0.30 m | Heavy but legible. A hard landing off the ledge bank's 1.45 m top. |
| **0.30 m+** | **Stops reading as impact and starts reading as the camera falling through the floor.** The focus drops a quarter of the avatar's own 1.20 m height. |
| **0.45 m+** | **Discomfort band.** 9 m/s of vertical focus travel on the attack. Third person is far more forgiving than first person here — the lagged spring arm and the visible body both anchor the frame — but this is where it stops being forgiving. |

**The range top is 0.50 m, above the discomfort threshold, deliberately.** The packet requires
that the range reach the edge of wrong; a range capped at 0.30 would never teach what too much
looks like. It requires only that the range not include it *quietly*. **MOVE-4d must label the
slider track: `impact` to 0.30, `heavy` to 0.45, `discomfort` beyond**, with the band name
shown beside the live value. Labelled, not hidden, and not locked.

**UNVERIFIED — reasoned, not measured.** Both the 0.30 and 0.45 boundaries are reasoned from
the geometry (focus travel per second against a 1.20 m body at a 3.15 m rest arm) and from the
general fact that periodic vertical viewpoint motion in the 2–4 Hz band drives simulator
sickness. **No one has measured them in this game.** They are the right shape for a first
labelling and MOVE-4e's headed pass plus Talon playing it is what settles them.

### 5.6 Why THRILL does not apply here, and exactly when it will

**THRILL does not apply to a lab knob.** The dip in `SandboxCamera`, tuned in a dev harness, is
an instrument: it exists so a number can be found. It makes no claim about what a player should
feel and it ships to no player.

**It applies the moment the dip reaches the actual game** — the first time a tuned value is
pasted into a shipping path and a landing in camp or in the woods is meant to *land* on
somebody. At that point "how heavy should a landing feel, and why there" is an affect question,
it goes through `/direct`, and `/direct` writes back to the THRILL ledger. **The deferral is
sequenced, not dropped**, and the trigger is named: **the first commit that gives a non-zero
`CameraDipStrengthM` default to anything outside `scenes/dev/`.**

---

## 6. The `MotorTuning` object

### 6.1 Shape

```csharp
namespace MpFoundation.Net;

/// One field per knob-table row. A value type: copied, never aliased, so a panel
/// editing a working copy can never half-apply it.
public readonly record struct MotorTuning
{
    // Ground
    public float MoveSpeed { get; init; }
    public float SprintMultiplier { get; init; }
    public float Acceleration { get; init; }
    public float Deceleration { get; init; }
    public float TurnAcceleration { get; init; }
    public float TurnLerp { get; init; }
    public float AimTurnLerp { get; init; }
    // Gravity
    public float Gravity { get; init; }
    public float FallGravityMultiplier { get; init; }
    public float ApexHangStrength { get; init; }
    public float ApexHangWindowMps { get; init; }
    // Jump
    public float JumpVelocity { get; init; }
    public float JumpReleaseGravityMultiplier { get; init; }
    public float CoyoteTimeSec { get; init; }
    public float JumpBufferSec { get; init; }
    // Air
    public float AirControlBuild { get; init; }
    public float AirControlTurn { get; init; }
    public float AirControlBrake { get; init; }
    // Skid
    public float SkidEnterSpeedFraction { get; init; }
    public float SkidAlignmentMax { get; init; }
    public float SkidDeceleration { get; init; }
    public float SkidExitSpeedMps { get; init; }
    public float SkidMaxSec { get; init; }
    // Landing
    public float LandMinFallMps { get; init; }
    public float LandFullFallMps { get; init; }
    public float TakeoffKickSec { get; init; }
    public float TakeoffKickMinDriveFraction { get; init; }
    // Camera
    public float CameraDipStrengthM { get; init; }
    public float CameraDipAttackSec { get; init; }
    public float CameraDipRecoverSec { get; init; }

    public static MotorTuning Default { get; }   // §8 — the identity contract
    public static MotorTuning Current { get; private set; }  // single writer: TryApply
}
```

Thirty fields, one per row, same names as the constants they replace — **same names
deliberately**, so the printed C# (§6.4) is a straight substitution and a reader grepping for
`Deceleration` finds both ends of the round trip.

`SkidEnterSpeedFraction` is the one renamed field (§2.5); the printed C# re-emits the derived
`SkidEnterSpeedMps = MoveSpeed * <fraction>f` form so `AvatarMotor.cs` keeps the declaration it
has today.

### 6.2 The file

- **Location:** `user://movement-tuning.json` — Godot's per-user data directory, resolved by
  the engine, outside the repo.
- **Not in the repo, and that is the decision.** A lab experiment must not be able to change
  the game for everyone by being committed. It is a per-machine artefact of a per-machine
  session. If a tuning is good enough to keep, the path for keeping it is the printed C# and a
  reviewed commit to `AvatarMotor.cs` — which is the whole point of §6.4.
- **Written on every `Apply`**, not only on quit: a lab session that ends in a crash or an
  Alt-F4 must not lose the setting, and "the settings he keeps" is the packet's stated value
  of the whole wave.
- A rolling copy of each printed tuning also lands in
  `user://movement-tuning-prints/<yyyy-MM-dd-HHmmss>.cs` (§6.4).

### 6.3 Format, load semantics, and every malformed case

```json
{
  "version": 1,
  "savedUtc": "2026-08-26T17:42:03Z",
  "values": {
    "Acceleration": 12.5,
    "ApexHangStrength": 0.35,
    "CameraDipStrengthM": 0.12
  }
}
```

**Sparse by design: `values` carries only the fields that differ from `MotorTuning.Default`.**
A full-fidelity dump would be thirty lines of which twenty-seven say "unchanged", and a
file that restates a default is a file that silently pins that default against a future change
to the shipped constant. Sparse means: *what Talon moved*, and nothing else.

Load, at playground startup only:

| Case | Behaviour |
|---|---|
| File absent | `Current = Default`. Silent — this is the first run and it is not a problem. |
| File present, JSON parse fails | **The whole file is discarded**, `Current = Default`, one warning line in the readout naming the file. Never a partial application: half a corrupt file is a tuning nobody authored. |
| `version` unknown / newer | Discarded as above, warning names the version. Forward compatibility is a promise this file does not make. |
| **Field missing from `values`** | **The shipped default is used** — this is the normal case, since the file is sparse by design. |
| Field present, unknown name | Ignored, one warning line naming it. A knob that was removed must not stop the file loading. |
| Field present, non-finite (`NaN` / `±Inf`) | **Replaced by the shipped default**, warning names it. NaN must never reach the motor: it propagates through `MoveToward` into position and the body is gone for good. |
| Field present, outside the knob's `[min, max]` | **Clamped to the range**, warning names the field, the requested value and the clamped value. Clamped rather than defaulted, because a hand-edited 25 for `Deceleration` clearly means "high", and defaulting to 21 would silently contradict an explicit intent. |
| Field present, breaches a **hard structural bound** (§2.3) or an ordering constraint | Clamped to the legal side of the bound, warning names the bound and the test or the rule behind it. |

**Every warning goes to the playground readout, not only to stdout.** A warning in a console
nobody is reading is a warning that did not happen — and this is a headed lab.

**MECHANICS §5, save/load, answered rather than left open:** the tuning survives process
restart; it does **not** survive a machine change, is not carried across a network session,
and is not part of any save-game. It is a lab preference file.

### 6.4 Print-as-C# — the exact text shape

**This is the deliverable of the whole wave. Vague here means the wave fails quietly.** A
`Print` button (and a keybind) writes the block below to three places at once: the Godot
console, the OS clipboard via `DisplayServer.ClipboardSet`, and
`user://movement-tuning-prints/<timestamp>.cs`. **The clipboard is not optional** — "paste it
back" is the workflow, and making Talon hunt a console line for it is how the round trip
quietly stops happening.

The exact output, for a tuning where three knobs moved and one of them is a breached pin:

```csharp
// ─── MotorTuning print — 2026-08-26 17:42:03 ───────────────────────────────────
// 3 of 30 knobs differ from the shipped defaults. Paste each line over the
// constant it names; the file and line are given so nothing has to be hunted.
//
// !! 2 PINNED KNOBS ARE OUT OF BOUNDS. THE SUITE WILL GO RED IF YOU PASTE THIS. !!
//
//    Deceleration 17.50 is outside [20.892, 21.080], asserted by
//      AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold
//      (tests/unit/AirborneControlTests.cs:217). Air-brake shed would be 3.727 m/s
//      against an asserted 4.47 +/- 0.02 -- and would fall BELOW the 4.050 m/s skid
//      entry speed, breaking MOVE-3a 2.4's "one threshold, two systems" rule.
//    Deceleration 17.50 is also below the 18.384 m/s^2 floor asserted by
//      EelProfileTests.Invariant1_BodyComesToRestBeforeControlReturns_AtEveryCharge
//      (tests/unit/EelProfileTests.cs:159). EelProfile.TotalFlightSec would be
//      1.225 s against a 1.200 s ragdoll window.
//    ApexHangStrength 0.35 at window 2.00 is inside its ceiling of 0.377, asserted by
//      AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately
//      (tests/unit/AirborneControlTests.cs:317-322). OK, with 7% of margin.
//
// TESTS THAT WILL FAIL ON PASTE, and the literals each one asserts:
//    AirborneControlTests.cs:217  Near(4.47f, ...) -- would need 3.727f
//    EelProfileTests.cs:159       TotalFlightSec < 1.2 -- no literal to edit; the
//                                 fix is a design decision about EelProfile, not a
//                                 number. See spec 3.4 argument 3.
//    LocomotionTests.cs:655       literal `> 18.4f`
//    LocomotionTests.cs:402       restates the Eel floor
//
// live invariant readout at print time:
//    EelProfile invariant 1 ........ 1.225 / 1.200 s      BREACHED
//    air-brake vs skid entry ....... 3.727 / 4.050 m/s    BREACHED
//    JumpApex window, apex ......... 1.683 (1.45 - 1.75)  holds
//    JumpApex window, airtime ...... 0.767 (0.650 - 0.770) holds
//    gear ordering ................. 2.43 < 5.40 < 8.64   holds
// ───────────────────────────────────────────────────────────────────────────────

// scripts/net/AvatarMotor.cs:85
    public const float Acceleration = 12.5f;              // was 9

// scripts/net/AvatarMotor.cs:102     [PINNED [20.892, 21.080] — BREACHED]
    public const float Deceleration = 17.5f;              // was 21

// scripts/net/AvatarMotor.cs:NEW     (add beside JumpReleaseGravityMultiplier)
    public const float ApexHangStrength = 0.35f;          // was 0
    public const float ApexHangWindowMps = 2.0f;          // was 2 (unchanged, printed
                                                          //  because ApexHangStrength
                                                          //  makes it live)

// ─── unchanged (27) — press Print All to include them ──────────────────────────
```

**Shape rules, all binding on MOVE-4d:**

1. **Only what moved is printed**, in knob-table order, grouped by owning file. `Print All`
   emits all thirty in the same shape.
2. **Every line is a complete, compilable C# declaration** at the indentation the target file
   uses (four spaces), so it can be pasted over the existing line with no hand-editing. That
   is the acceptance test for this format: *paste, save, build, no edits.*
3. **`// was <old>`** on every line, using the shipped default, not the previous session's
   value. It is what makes a paste reviewable in a diff.
4. **The file and line of the constant** in a comment above each. Line numbers go stale; the
   comment says which file authoritatively, and the line is a hint.
5. **Float literals always carry the `f` suffix and are printed with the minimum number of
   decimals that round-trips the slider's step** — `12.5f`, not `12.500000f`, and never
   `12.5` (which is a `double` and will not compile into a `float` const).
6. **A breached pin gets the banner, the per-line `[PINNED … BREACHED]` tag, and the arithmetic
   of the breach.** Not a generic "this may break tests".
7. **The live invariant readout is printed too**, breached or not, so the pasted block records
   what the state of the world was when the tuning was chosen.
8. **New constants print with `NEW`** and a placement hint rather than a line number.
9. **`SkidEnterSpeedFraction` prints as the derived form**:
   `public const float SkidEnterSpeedMps = MoveSpeed * 0.75f;`
10. **A `TESTS THAT WILL FAIL ON PASTE` block, naming each test and the literal it asserts.**
    This is the item §3.2b forces: with eight knobs frozen to ±0.1% by hand-typed literals, a
    paste is almost never a one-file edit, and **the printed block is the only place the full
    edit set can be known.** Where a pin is a *property* rather than a literal (the `EelProfile`
    inequality has no number to retype — the fix is a design decision), the block says so
    explicitly rather than pretending there is a literal to change.
11. **A live but unchanged knob is printed anyway when it becomes live**, with `// was <x>
    (unchanged, printed because …)`. `ApexHangWindowMps` at its default is inert while strength
    is zero and load-bearing the moment it is not; a paste that moved strength and silently
    omitted the window would be a paste that does not reproduce what was played.

---

## 7. The prediction-parity guard

### 7.1 The hazard, precisely

`MotorTuning.Current` is ambient mutable state read by server-authoritative, client-predicted
code. In the lab — one process, one simulation, no prediction — that is exactly right and is
the only way to get a live slider without threading a tuning argument through `Step` and its
290 call sites.

In a networked context it is a silent desync generator. If the server holds tuning A and a
predicting client holds tuning B, both simulate happily; the divergence per tick is
`½(a_A − a_B)dt²`, reconciliation pulls the client back every snapshot, and **the symptom is
continuous rubber-banding, not an error.** It looks exactly like a bad connection.

### 7.2 The mechanism chosen: a hard runtime refusal in the single writer, in every build

```csharp
/// The ONLY writer of MotorTuning.Current. Refuses while any multiplayer session exists,
/// in EVERY build configuration.
public static bool TryApply(in MotorTuning next, out string refusal)
{
    if (SessionLive())
    {
        refusal = "MotorTuning cannot be changed while a network session is live: the server "
                + "and every predicting client must simulate identical constants.";
        return false;                       // refuse. Do not throw.
    }
    refusal = "";
    Current = Validate(next);               // §6.3's clamps
    return true;
}
```

`Current`'s setter is `private`. `TryApply` is the single writer — the same single-writer shape
`.claude/rules/single-writer.md` requires and that `camp_gen.gd` and the physics-authority row
of `STATE-CASCADE-TABLE.md` already use.

**Why refuse rather than throw:** the caller is a UI slider callback. A throw there kills the
panel mid-drag and leaves the tuning half-applied across fields, which is a worse state than
the one being prevented. A `false` plus a refusal string the panel can display is recoverable.

**`SessionLive()`** asks the engine, not a bookkeeping flag: `Engine.GetMainLoop() as SceneTree`
→ `GetMultiplayer()` → a peer exists and is not disconnected. Asking the engine is what makes
the guard **unforgettable** — a future join path cannot fail to set a flag it never has to set.
*(Implementation caveat for MOVE-4b: Godot 4.x installs an `OfflineMultiplayerPeer` by default
in some configurations. The check must treat that as **not live**. Verify against the actual
4.7 API rather than assuming `MultiplayerPeer is null` is the offline case.)*

### 7.3 Why not each of the alternatives

| Option | Why not |
|---|---|
| **A write path only the lab scene can reach** | It is a convention, and conventions are what this repo's failure log is made of. It also fails on its own terms: nothing stops the lab scene being loaded inside a session. **Kept as the *shape* of the API — `TryApply` is the only writer and `Current`'s setter is private — but not as the guard.** |
| **`Debug.Fail` / `Assert` on mutation** | The right *detector*, wrong *mechanism*: both are compiled out of release. The guard would be absent from exactly the build where the failure is silent and unrecoverable, which is the opposite of what a guard is for. The runtime refusal above is the same check that survives the compiler. |
| **A compile-time flag excluding the mutator from export builds** | Worst of the three, and it is worth saying why plainly: it makes the debug build and the release build **different simulations**. That is the one thing the whole `AvatarMotor` design exists to prevent — its class doc says the server simulating the same step from the same inputs is what makes movement cheats structurally impossible. A guard that only exists in one build configuration undermines the property it protects. **Rejected.** |

**Belt and braces, cheap, and specified so it is not forgotten:** an xUnit test asserting
`TryApply` returns `false` and leaves `Current` unchanged when a session is live, plus one
asserting it returns `true` and applies when it is not. Two tests, no engine needed if
`SessionLive` is injectable.

### 7.4 What the failure looks like if the guard is defeated

**Not an exception, not a log line, not a desync error.** In order of what an observer sees:

1. **Rubber-banding proportional to acceleration.** Worst during sprint ramps, skids and jump
   arcs; **absent while standing still**, because two motors integrating nothing diverge by
   nothing. That "fine when idle, awful when moving" signature is the diagnostic.
2. **Asymmetric between players.** Only the peer whose tuning differs from the server's is
   affected; everyone else sees that player's proxy moving smoothly, because a proxy is
   interpolated from authoritative snapshots and never predicted. **So the player experiencing
   it is the only person who can see it**, which is how it survives a playtest.
3. **`DesyncMonitor` catches only the large case, and MOVE-4b must not treat it as the net.**
   It reports a sustained divergence at ≥ 1.0 m for 10 consecutive snapshots. A gross mismatch
   (`Gravity` 22 vs 14) trips it. A plausible one (`Acceleration` 9 vs 9.5) produces sub-metre
   corrections that reconcile away every snapshot and **never trip it at all** — while still
   feeling wrong to play. It is a second line, not the guard.
4. **It would be misdiagnosed as a network problem, for a long time.** Everything about the
   presentation — pops, snaps, a player who "feels laggy" — points at the connection. That is
   the real cost, and the reason the guard is a hard refusal rather than a warning.

---

## 8. The default-identity contract

**Numbered, because the packet asks for it as acceptance criteria and because "the defaults are
the same" is the kind of claim that is assumed rather than checked.**

**AC-D1.** `MotorTuning.Default` has exactly thirty fields, one per knob-table row.

**AC-D2.** **Every field of `MotorTuning.Default` is the same literal as the constant it
replaces**, transcribed character for character. Twenty-eight of the thirty are direct
transcriptions from `AvatarMotor.cs` and `AvatarVisual.cs`.

**AC-D3.** The two exceptions, both stated, neither a change in behaviour:

- **`SkidEnterSpeedFraction = 0.75f`** is *not* a transcription of `SkidEnterSpeedMps`, because
  that constant is `MoveSpeed * 0.75f` and this field is the fraction, not the product. **The
  derived value is identical**: `0.75 × 5.4 = 4.05 m/s`, which is what `SkidEnterSpeedMps` is
  today, exactly. Reshaped, not retuned (§2.5).
- **`CameraDipStrengthM = 0.00f`, `CameraDipAttackSec = 0.05f`, `CameraDipRecoverSec = 0.26f`**
  have no constant to transcribe — `SandboxCamera` has no dip. **The identity that matters is
  behavioural, and it is exact: at strength 0.00 the dip contributes nothing to `focusHeight`
  and the camera is bit-identical to today's.** The two duration defaults are inert while
  strength is zero and are starting positions, not behaviours. (`0.26` is chosen to agree with
  `AvatarVisual.LandAbsorbSec`; see §5.4.)

**AC-D4.** `ApexHangStrength`'s default of `0.00f` is the exact no-op: `GravityFor` returns
before touching the window, and its output is bit-identical to today's for every input,
including the `locked` and `ballistic` paths.

**AC-D5.** **MOVE-4e's headed capture, run on `MotorTuning.Default` with every slider
untouched, must measure `apex 1.534 m / distance 6.192 m / airtime 0.717 s` (held sprint jump)
and `apex 0.467 m / distance 1.710 m / airtime 0.317 s` (jog tap), to three decimals** — the
MOVE-3d figures, byte for byte, from `powershell -File tests/Run-MovementPlayground.ps1 -Capture`.
Any deviation at all in the third decimal is a defect in the seam, not a rounding artefact:
these are deterministic fixed-tick figures, not measurements with noise.

**AC-D6.** **No value in this document is a retune.** Every default is today's shipped value.
Where this spec argues a value should move, it does so as a *recommended first experiment*
(§4.4: 0.35 / 2.00; §5.5: 0.12 m) which the panel starts nowhere near.

**AC-D7.** With `MotorTuning.Current == MotorTuning.Default`, the full suite result must be
unchanged from the branch's pre-MOVE-4 result. **This spec does not claim that it is** — no
suite was run for this packet, by instruction. It is the acceptance criterion MOVE-4b owes.

---

## 9. What MOVE-4b must not break — the const-to-property seam

**This section exists because the seam is not free, and the cost is invisible until the
compiler finds it.**

### 9.1 The compile-time dependency set

`AvatarMotor`'s values are `public const`, and **`const` propagates**: other classes declare
their own `const`s *from* them. A `const` cannot be initialised from a property, so **converting
any of these to a static property is a compile error in every downstream declaration**, not a
silent behaviour change. MOVE-4b must plan for it rather than discover it.

| Downstream declaration | File:line | Reads |
|---|---|---|
| `EelProfile.LaunchVerticalFullMps` | `scripts/game/prey/EelProfile.cs:87` | `JumpVelocity` |
| `EelProfile.RiseSec` | `scripts/game/prey/EelProfile.cs:223` | `Gravity` |
| `EelProfile.PeakHeightM` | `scripts/game/prey/EelProfile.cs:228` | `Gravity` |
| `EelProfile.FallSec` | `scripts/game/prey/EelProfile.cs:234` | `Gravity`, `FallGravityMultiplier` |
| `EelProfile.SkidSec` | `scripts/game/prey/EelProfile.cs:246` | `Deceleration` |
| `EelProfile.SkidDistanceM` | `scripts/game/prey/EelProfile.cs:249` | `Deceleration` |
| `LocomotionProfile.WalkSpeedMps` | `scripts/game/sandbox/LocomotionProfile.cs:75` | `MoveSpeed` |
| `LocomotionProfile.JogSpeedMps` | `scripts/game/sandbox/LocomotionProfile.cs:78` | `MoveSpeed` |
| `LocomotionProfile.SprintSpeedMps` | `scripts/game/sandbox/LocomotionProfile.cs:81` | `MoveSpeed`, `SprintMultiplier` |
| `ToolStance.RunEnterSpeedMps` | `scripts/game/stance/ToolStance.cs:220` | `MoveSpeed` |
| `ToolStance.RunExitSpeedMps` | `scripts/game/stance/ToolStance.cs:225` | `MoveSpeed` |
| `AvatarMotor.SkidEnterSpeedMps` | `scripts/net/AvatarMotor.cs:150` | `MoveSpeed` (in-file) |
| `AvatarMotor.AirWishSpeedFloorMps` | `scripts/net/AvatarMotor.cs:287` | `MoveSpeed` (in-file) |
| `GreyboxPlayerLab.WalkSpeed` | `scripts/dev/GreyboxPlayerLab.cs:48` | `MoveSpeed` |
| `GreyboxPlayerLab.RunSpeed` | `scripts/dev/GreyboxPlayerLab.cs:50` | `MoveSpeed`, `SprintMultiplier` |
| `ToolStanceTests.RunSpeed` | `tests/unit/ToolStanceTests.cs:58` | `MoveSpeed`, `SprintMultiplier` |
| `ArrowBallisticsTests.Gravity` | `tests/unit/ArrowBallisticsTests.cs:17` | `Gravity` |
| `EelProfileTests.cheatedVertical` (local) | `tests/unit/EelProfileTests.cs:137` | `JumpVelocity` |
| **`[InlineData]` argument** | **`tests/unit/AirborneControlTests.cs:254`** | `Gravity`, `FallGravityMultiplier` |
| **`[InlineData]` argument** | **`tests/unit/AirborneControlTests.cs:255`** | `Gravity`, `FallGravityMultiplier` |
| **`[InlineData]` argument** | **`tests/unit/AirborneControlTests.cs:256`** | `Gravity` |
| **`[InlineData]` argument** | **`tests/unit/AirborneControlTests.cs:257`** | `Gravity` |
| **`[InlineData]` argument** | **`tests/unit/AirborneControlTests.cs:258`** | `Gravity`, `JumpReleaseGravityMultiplier` |

**The five `[InlineData]` rows are the ones that will surprise MOVE-4b**, because they are not
declarations and a grep for `const` misses them. **An attribute argument must be a compile-time
constant and there is no `static readonly` escape hatch for it.** Converting `Gravity`,
`FallGravityMultiplier` or `JumpReleaseGravityMultiplier` therefore forces those five rows to
be rewritten as literals or moved to `MemberData`. Neither is hard; discovering it at the end
of the wave rather than the start is.

**Transitive** — these break only if the derived `const` above is *also* converted:
`tests/unit/FaunaBrainTests.cs:210-212` (off `LocomotionProfile`) and
`scripts/game/world/NightPressureGuarantee.cs:110` (`LoadedWalkSpeedMps`).

**Already safe, and the model for §9.2:** `scripts/game/archery/Arrow.cs:37` declares
`private static readonly float Gravity = AvatarMotor.Gravity` and survives untouched.

**`EelProfile` is already half-converted, which makes the job smaller than it looks.** The
split there is exactly "does the expression need a runtime call": `LaunchVerticalFullMps` (87),
`RiseSec` (223), `PeakHeightM` (228), `SkidSec` (246) and `SkidDistanceM` (249) are `const`;
`FallSec` (233), `FlightSec` (237), `FlightDistanceM` (241), `TotalFlightSec` (255),
`TotalDisplacementM` (261) and `SelfEjectCharge` (267) are **already `static readonly`**,
because everything downstream of `FallSec`'s `Mathf.Sqrt` had to be. **Five members convert and
the whole file goes live in one commit.**

*(`IncapacitationSelfTest.Dt`, `WaterSelfTest.Dt`, `AirborneControlTests.Dt:36` and
`SandboxSelfTest`'s local `dt` at `:1511`/`:1650` read `TickDelta`, which stays a `const`
(§2.4) — they are unaffected.)*

### 9.2 The recommendation, and its consequence stated honestly

**Recommendation: convert each downstream `const` to `static readonly` (or an expression-bodied
`static` property) in the same commit that converts the `AvatarMotor` constant it reads.** They
are all already computed once from other constants; `static readonly` produces the same value
at the same cost and can read a property.

**The consequence, which must be *chosen* rather than absorbed:** `LocomotionProfile`'s gears,
`ToolStance`'s run hysteresis and `EelProfile`'s whole flight arithmetic then become **live** —
they track the `MoveSpeed` and `Gravity` sliders in real time. **That is almost certainly what
Talon wants** (a `MoveSpeed` slider that does not move the gait's gear thresholds is a slider
that makes the character run at the wrong animation speed), and it is what makes the §3.4 live
invariant readout possible at all. But it is a genuine widening of the blast radius and it is
MOVE-4b's to confirm, not this spec's to assume.

**One conversion that must NOT happen:** `AvatarMotor.TickRate` and `TickDelta` stay `const`.
They alias `NetProfile` and they are the simulation rate, not feel (§2.4).

### 9.3 A live defect the sliders expose — `ResolveYaw`'s unclamped lerp

Found while setting row 6's range, and reported because it is a real bug rather than a range
question:

```csharp
if (faceYaw is float aimed && float.IsFinite(aimed))
    return Mathf.LerpAngle(prevYaw, aimed, Mathf.Min(1f, AimTurnLerp * dt));   // clamped
if (wish.LengthSquared() > 0.05f)
    return Mathf.LerpAngle(prevYaw, Mathf.Atan2(-wish.X, -wish.Z), TurnLerp * dt);  // NOT clamped
```

The aimed path clamps the lerp factor; the travel path does not. At 60 Hz any `TurnLerp` above
60 produces a factor above 1 and the facing overshoots and then oscillates about its target —
and the overshoot magnitude depends on `dt`, so the behaviour is frame-rate dependent in a
function the netcode requires to be deterministic. It cannot happen today (`TurnLerp` is 12,
and hard-coded), which is why nobody has seen it.

**Recommendation for MOVE-4b: add the same `Mathf.Min(1f, …)` to the travel path.** Row 6's
max of 45 holds either way; the fix is one call and it removes a class of bug rather than a
range.

---

## 10. Bible check

```
Bibles applied:  MECHANICS (the gravity term's boundaries and idempotency, the dip's
                 trigger, the flicker rule, the numeric extremes, and save/load);
                 INTERACTION (the jump button's meaning, feedback at the moment of the
                 trigger, and the never-costs-a-frame rule).
                 LEVEL does not apply — MOVE-3a declared the playground a dev harness,
                 not a level, and that stands.
                 THRILL does not apply to a lab knob; §5.6 names the trigger at which it
                 will, so the deferral is sequenced rather than dropped.
                 BEHAVIOR does not apply — no autonomous entity is touched. The one
                 adjacency (EelProfile's blast arc) is handled by keeping the apex term
                 gated off for `locked` and `ballistic` bodies, §4.2.
Items checked:   MECHANICS §1 (boundary conditions), §2 (state transition integrity /
                 the flicker rule), §4 (idempotency), §5 (save-load persistence),
                 §6 (numeric rules at the extremes);
                 INTERACTION §2 (feedback at the moment of the trigger),
                 §4 (range and timing / tap vs hold), §6 (reversibility).
Result:          Pass, with three findings raised rather than absorbed — §9.1 (the
                 const-to-property seam is not free), §9.3 (ResolveYaw's unclamped
                 lerp), §5.2 (two intensity curves already exist off one landing gate).
```

**MECHANICS §1 — boundary conditions, stated not inherited.** `GravityFor`'s existing strict
boundaries are unchanged: `v_y < 0` is falling, `v_y > 0` with the key up is the cut, `v_y == 0`
takes plain `Gravity`. The apex term adds two more and both are stated: at `|v_y| == window`
exactly, `u == 1`, `w == 0` and the term contributes nothing (the window is **exclusive** at its
outer edge); at `v_y == 0` exactly, `u == 0`, `w == 1` and the term is at full strength — which
is why `ApexHangStrength` is capped at 0.90 rather than 1.00, since 1.00 at that exact point is
zero gravity and a body that never leaves its apex. The dip's gate is inclusive at
`LandMinFallMps` (`fall >= 2.5`), matching `SandboxAvatar.cs:2560` and `AvatarVisual.cs:3339`
exactly rather than approximately.

**MECHANICS §2 — the flicker rule, and the thing that would have turned out to be a latch.**
Both new terms are checked against it and neither is a state. The apex hang has no enter, no
exit and no flag: it is a pure function of `|v_y|`, so there is nothing to chatter, and the
smoothstep weight means even the window edges are approached with zero slope. The camera dip
*does* have a lifetime, and it is handled as an idempotent envelope rather than a state: it has
exactly one entry (a qualifying touchdown), one exit (`t >= A + R`) that depends on nothing the
player does, and no path that holds it open — the same property `SkidMaxSec` exists to give the
skid. **The tempting wrong design was a `bool _dipping` plus a spring; that is a latch, and it
is what §5.4 refuses.**

**MECHANICS §4 — idempotency.** `ActorEvent.Land` can fire again while a dip is still
unwinding (two landings 40 ms apart on the hop chains). Specified in §5.4: **restart at the
max, never sum.** Summing is the double-apply the bible names.

**MECHANICS §5 — persistence, decided rather than left open.** §6.3: the tuning survives
process restart via `user://`, does not survive a machine change, is not in any save-game and
never crosses the wire.

**MECHANICS §6 — numeric rules at the extremes.** Every knob's zero, max and negative case is
walked in §2.3, and three of them turn out to matter: `ApexHangStrength = 1.0` is an exitless
apex, `ApexHangWindowMps = 0` is a divide-by-zero on every airborne tick, and
`JumpReleaseGravityMultiplier < 1` inverts the variable jump. All three are hard-clamped in the
validator, not merely in the widget, so a hand-edited JSON cannot reach them.

**INTERACTION §2 — feedback at the moment of the trigger.** The camera dip *is* feedback, and
its timing is specified against the event rather than against a frame: the envelope starts on
the same tick the pose absorb and the `ActorEvent.Land` sound start, off the same gate and the
same fall speed. Three channels, one trigger instant. §5.1's threshold argument is what keeps
them from disagreeing about whether the trigger happened at all.

**INTERACTION §4 — range and timing, tap vs hold.** The jump button's meaning is unchanged by
this packet and is stated so it cannot drift: **Space is press-to-jump and hold-for-height**,
resolved through `MoveIntent.Jump` (the edge) and `MoveIntent.JumpHeld` (the level), exactly as
MOVE-3b shipped at protocol v12. The apex hang **does not change what the button means** — it
changes the shape of the arc the button already commands, at both ends of its range. **No new
input, no new binding, no second meaning on the key** (see Open questions for the fork the
orchestrator is holding on that subject).

**INTERACTION §6 — reversibility.** Every knob is freely repeatable and reversible: a `Reset
to shipped defaults` button per group and one for all thirty, which is `TryApply(Default)` and
therefore goes through the same validator and the same guard.

**INTERACTION, "weight is skin, not friction" — the standing rule, checked explicitly.**
Nothing in this spec can cost a frame of input responsiveness, and the reason is structural in
both cases rather than careful. The apex hang changes an acceleration the body was already
integrating; it delays no input, gates no input and holds no impulse back — MOVE-3a's rejected
pre-takeoff crouch is the shape it deliberately is not. The camera dip lives entirely in
`SandboxCamera`, client-local presentation, and never sees a `MoveIntent` at all.

---

## 11. Open questions — ripe, and not resolved here

**11.1 — Space means two things, and it is the orchestrator's fork, not mine.** Holding Space
means "tall jump" today (MOVE-3b, protocol v12); the Movement Feel Lab brief also wants it to
mean "slide". **I am not resolving it and I have not designed around either answer.** The view
the packet invited, offered as input and nothing more: **the collision is real and it is
resolvable by context rather than by a second key** — a hold that begins *airborne* is
unambiguously the jump's height hold, and a hold that begins *grounded at speed* is
unambiguously a slide, because the two states are mutually exclusive at the instant the press
lands. What that costs is a rule the player has to learn without being told, and MOVE-3a §6.2's
buffered-jump edge rule (a press *edge* only, never a held key) is the precedent that says this
repo already prefers edges to levels for exactly this reason. **Trigger for the fork becoming
ripe: after Talon has played a tuned MOVE-4 and MOVE-5 has a slide worth binding.** Until then
there is nothing to decide between.

**11.2 — Two intensity curves already exist off one landing gate.** `AvatarVisual.cs:3341`
clamps landing intensity to `[LandMinIntensity, 1]`; `SandboxAvatar.cs:2562`, the
`ActorEvent.Land` fan-out, clamps the same quantity to `[0, 1]`. Same gate, same fall speed,
two answers — so the sound and the pose already disagree slightly about how hard a marginal
landing was. **The dip follows `AvatarVisual`'s** (§5.2) so it does not become a third opinion.
**Options:** (a) unify on `AvatarVisual`'s floored curve, which makes a marginal landing's sound
audible where it is currently near-silent; (b) unify on `SandboxAvatar`'s unfloored curve, which
makes a marginal landing's pose invisible and reintroduces the "reads as a bug just past the
gate" problem `LandMinIntensity` was added to fix; (c) leave both, documented. **Recommendation
(a)**, but it changes audio at the margin and belongs in a packet that owns `ActorFx`, not this
one. **Trigger: the first time somebody tunes `LandMinFallMps` in the panel and notices the
sound and the knees disagree** — which MOVE-4 makes likely for the first time.

**11.3 — Does the `MoveSpeed` slider make `LocomotionProfile`'s gears live, and is that
wanted?** §9.2 recommends yes and gives the argument. It is a value-shaped question with a
direction-shaped tail: the gait, the camera speed cue, the tool-stance hysteresis and the
skid threshold all key off the same number, and making them live is what stops a `MoveSpeed`
slider producing a character running at the wrong animation cadence. **Flagged rather than
escalated** — MOVE-4b confirms it in code, and only if it turns out that Talon wants the gears
pinned while the speed moves does this become a fork for him.

**11.4 — `EelProfile` constrains the movement knobs, and the kit it protects is off.** Four
knobs are floored or ceilinged by invariants belonging to `EelProfile`, which ships behind
`EelKitFlag`, disabled. (They are no longer the *tightest* pins — §3.2 corrects that — but they
are the only ones whose fix is a design decision rather than a retyped literal, because
`Invariant1` asserts an inequality with no number to edit.) If Talon lands on a `Deceleration`
below 18.384, the correct response is almost certainly to re-derive `EelProfile` rather than to
give up the movement feel — but that is a call about the eel kit and needs whoever owns it.
**Trigger: the first printed tuning that breaches it.** The panel is built to make that moment
loud (§3.4, §6.4) rather than to prevent it.

**11.5 — A large part of the movement suite asserts the current value rather than a property,
and MOVE-4 is what makes that visible.** Eight knobs are frozen to within ±0.1% (§3.2b) and
`MoveSpeed` is frozen to *exactly* 5.4 by an untoleranced `Assert.Equal` against a hand-typed
literal in a mirror class. This is not a defect — drift-catching is a real job and this repo has
been bitten by drift — but it means **every deliberate retune is a multi-file edit whose full
extent is only knowable from outside the lab**, which is why §6.4 makes the printed block carry
the list. **The question it raises is whether some of those freezes should become
tolerance-widened property assertions** — e.g. `AirControlBrake < AirControlTurn <
AirControlBuild` and `InRange(0.30, 0.60)` (both of which `AirborneControlTests` *already*
asserts at `:62`/`:64`/`:66`) do the design's real job, while the `[InlineData(…, 0.30f)]`
freeze on top of them does only the drift job. **Options:** (a) leave everything as is and rely
on the printed edit list; (b) keep the freezes but move each one into a single named
`ShippedTuningSnapshot` test per group, so a retune is one obvious file to update instead of
eight scattered assertions; (c) widen the freezes into property assertions and lose the drift
guard. **(b) is the shape this spec would argue for** — it keeps both jobs and makes the retune
cost one edit — but it is a change to the test architecture, it touches files this packet does
not own, and it is not worth doing speculatively. **Trigger: the first time Talon prints a
tuning he actually wants to keep** and someone has to perform the paste. That is the moment the
cost is real and the right shape is obvious; before then it is a refactor in search of a reason.

**11.6 — `ApexHangWindowMps` has no effect while `ApexHangStrength` is zero, and the panel
should probably say so.** A slider that does nothing at the current setting of another slider
is a small lie of the kind §2.4 refuses for the two excluded constants. It is not the same case
— it becomes live the instant strength leaves zero — but MOVE-4d should dim it, not hide it,
while strength is 0. **Value-shaped, settled here as a recommendation; MOVE-4d may overrule it
on layout grounds.**
