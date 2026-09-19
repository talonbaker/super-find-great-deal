# Airborne control, variable jump height and landing — the spec

**Packet:** MOVE-3a · **Role:** systems-design · **Date:** 2026-08-26
**Branch:** `feat/2026-08-26-move-3-polish`
**Implements:** MOVE-3b (programming) transcribes this document. Every number below is a
constant name plus a value; nothing here is left to the coder's taste.

**Audited against code at `edef4e54`:** `scripts/net/AvatarMotor.cs`,
`scripts/game/sandbox/MoveIntent.cs`, `scripts/net/MoveState.cs`,
`scripts/net/NetCodec.cs`, `scripts/net/NetProfile.cs`,
`scripts/game/sandbox/LocomotionProfile.cs`, `scripts/game/water/WaterGeometry.cs`.

---

## 0. The one-paragraph version

The ground layer is done and is not touched. Three things are added inside
`AvatarMotor.Step`: **airborne acceleration runs at a fraction of ground acceleration**
(three fractions, one per job, all inside Talon's 30–60% window); **an airborne body's
wish speed is capped by the speed it already has**, so nobody gains speed in the air; and
**releasing the jump key while still rising triples gravity**, which is a release-cut
variable jump that costs no new `MoveState` field and no snapshot layout change. A landing
costs nothing — horizontal speed survives intact, deliberately, because a hop chain that
decays is not a hop chain. One new wire field (`MoveIntent.JumpHeld`), one new buttons
byte, one protocol bump.

---

## 1. What is not being changed

Restated so MOVE-3b does not have to infer it.

| Constant | Value | Status |
|---|---|---|
| `MoveSpeed` | 5.4 | unchanged (MOVE-1/CATCH-1 signed off) |
| `SprintMultiplier` | 1.6 | unchanged |
| `Acceleration` | 9 | unchanged |
| `Deceleration` | 21 | unchanged |
| `TurnAcceleration` | 34 | unchanged |
| `Gravity` | 22 | unchanged |
| `FallGravityMultiplier` | 1.35 | unchanged |
| `JumpVelocity` | 8.4 | unchanged |
| `CoyoteTimeSec` | 0.12 | **confirmed**, see §7 |
| `JumpBufferSec` | 0.12 | **confirmed**, see §7 |
| every `Skid*` constant | — | unchanged |
| `LocomotionProfile` (gears, cadence, lean) | — | out of packet |

Derived gear speeds used throughout this document: walk **2.43 m/s**
(`LocomotionProfile.WalkSpeedMps`), jog **5.40 m/s**, sprint **8.64 m/s**.

---

## 2. The airborne control model

### 2.1 Three fractions, not one

`RateFor` returns one of three rates and they do three different jobs. Scaling all three
by one number would have been simpler, and it is wrong here, because Talon's feel target
names two of the three specifically: *"momentum you leave the ground with is momentum you
keep"* and *"not full omnidirectional drift"*. Those are statements about **brake** and
**redirect**. **Build** is the one that is safe to leave relatively generous, because
§2.3's speed ceiling already bounds what it can ever produce.

```
public const float AirControlBuild = 0.45f;   // × Acceleration      = 4.05 m/s²
public const float AirControlTurn  = 0.35f;   // × TurnAcceleration  = 11.90 m/s²
public const float AirControlBrake = 0.30f;   // × Deceleration      = 6.30 m/s²
```

All three are inside 30–60%. The ordering `Brake < Turn < Build` is the design, and each
rank has a reason:

- **Brake is lowest (30%)** because braking to a stop in mid-air is the exact defect being
  fixed. It gets the least authority of the three.
- **Turn is next (35%)** because redirect is what converts a committed run into a
  free-floating drift. It must be enough to steer with and not enough to reverse with.
- **Build is highest (45%)** because a standing jump with no forward speed has nothing to
  redirect and nothing to keep; letting it gain a little speed toward a gap is the
  affordance that makes a standing jump worth having at all, and §2.3 caps it at jog
  speed.

### 2.2 What it multiplies, and what it does not

`RateFor` gains a `bool grounded` parameter, **defaulting to `true`** so every existing
call site and every existing pure-function test compiles and passes byte-identically.

```
public static float RateFor(Vector3 velocity, Vector3 wish, bool grounded = true)
```

The body is the shipped body with the three constants selected per branch:

- no input → `grounded ? Deceleration : Deceleration * AirControlBrake`
- standing start (flat speed ≤ 0.35 m/s) → `grounded ? Acceleration : Acceleration * AirControlBuild`
- otherwise → `Mathf.Lerp(turnRate, buildRate, Mathf.Clamp(alignment, 0f, 1f))`, where
  `turnRate` and `buildRate` are the ground pair or the air pair by the same switch.

The call site in `Step` becomes `RateFor(velocity, wish, prev.Grounded)`.

**It multiplies nothing else.** Not `MoveSpeed`, not `SprintMultiplier`, not the skid
rates, not `TurnLerp`/`AimTurnLerp` (facing is unchanged in the air — the body still faces
travel, per BEHAVIOR §2), not gravity, not `EelProfile.SkidSec` (which derives off
`Deceleration` itself and must **never** be re-derived off the air brake).

**`prev.Grounded`, not a live query.** Same convention gravity and coyote already use, and
it has two one-tick consequences that are stated rather than discovered: the tick a jump
fires still runs ground rates (`prev.Grounded` is true — this is wanted; the launch tick
keeps ground authority), and the tick the body touches down still runs air rates. One tick
is 16.7 ms at 60 Hz. Neither is a bug.

### 2.3 The airborne wish-speed ceiling

**Rule:** while airborne, the wish speed is the smaller of what the player asked for and
`max(current horizontal speed, the non-sprint ground wish speed)`.

```
public const float AirWishSpeedFloorMps = MoveSpeed;   // 5.40 m/s at speedFactor 1, dry
```

In `Step`, replacing the two lines that currently build `wish`:

```
float groundWish = MoveSpeed * sf * waterMul;                 // the floor, already computed
float desired = groundWish;
if (effective.Sprint && WaterGeometry.SprintAllowed(water) && dir.LengthSquared() > 0.01f)
    desired *= SprintMultiplier;
if (!prev.Grounded)
{
    var flat = new Vector2(prev.Velocity.X, prev.Velocity.Z);
    float flatSpeed = flat.Length();
    if (float.IsFinite(flatSpeed))
        desired = Mathf.Min(desired, Mathf.Max(flatSpeed, groundWish));
}
Vector3 wish = dir * desired;
```

The floor is expressed as `groundWish` rather than the bare constant on purpose: it is
already scaled by `speedFactor` and by the water multiplier, so a wading hop cannot exceed
wading speed and a heavily-laden player cannot exceed their carry-limited speed. The named
constant `AirWishSpeedFloorMps` exists so the value has a name in the file; the code reads
the scaled version.

**What this produces, case by case** (MECHANICS §6 — the extremes, checked):

| Takeoff | Input in air | Ceiling | Result |
|---|---|---|---|
| sprint 8.64 | hold forward + sprint | 8.64 | holds; **cannot gain** |
| sprint 8.64, decayed to 8.0 | hold forward + sprint | 8.00 | cannot re-accelerate; ratchets down only |
| jog 5.40 | presses sprint mid-air | 5.40 | **sprint does nothing in the air** |
| standing 0 | hold forward | 5.40 | builds at 4.05 m/s²; reaches 2.88 m/s by the end of a full jump |
| sprint 8.64 | releases sprint, holds forward | 5.40 | decays toward 5.40 at the *build* rate, not the brake rate |

That last row is an inherited shape, not a new one: on the ground, releasing sprint while
still holding a direction also decays at `Acceleration` (9) rather than `Deceleration`
(21), because `RateFor`'s aligned branch is what applies. Air behaves the same way. This is
consistency, and it is stated so nobody "fixes" it.

**Why a ceiling rather than a remembered takeoff speed.** A `MoveState.LaunchSpeed` field
would be marginally more faithful to "the speed you left with" — and it costs a second
snapshot layout change (the snapshot flags byte has been documented full since v10, so it
would be four more bytes on every packet for every player, forever). The ceiling is a pure
function of state already on the wire and produces the same answer in every case a player
can perceive. Performance and reliability over fidelity, per the standing tenets.

### 2.4 The momentum-keep proof

The number that matters is **how much speed the air brake can shed in the longest possible
jump**: `AirControlBrake × Deceleration × 0.710 s` = `6.30 × 0.710` = **4.47 m/s**.

- A **sprint** jump (8.64) with input released for the whole flight lands at **4.17 m/s** —
  48% of sprint retained. It cannot be cancelled.
- A **jog** jump (5.40) with input released lands at **0.93 m/s**, and reaches zero only at
  0.857 s — **longer than the 0.710 s maximum airtime**. It cannot reach a mid-air stop.
- A **walk** hop (2.43) *can* be brought to a mid-air stop, in 0.386 s. This is deliberate
  and correct: a hop in place is a thing every game has, and a walk has no committed
  momentum to protect.

The boundary sits at **4.47 m/s**, which is just above `SkidEnterSpeedMps` (4.05 m/s).
That coherence is worth stating as a rule in its own right: **any run committed enough to
skid on the ground is committed enough that it cannot be cancelled in the air.** One
threshold, two systems, no second number to keep in step.

For comparison, today's behaviour: at `Deceleration = 21` a sprint reaches a dead stop in
0.41 s, comfortably inside a 0.710 s jump. That is the defect.

### 2.5 The redirect proof

Using the shipped per-axis `MoveToward` (see §9, flagged), at `AirControlTurn × TurnAcceleration = 11.90 m/s²`:

| Manoeuvre | Δv needed | Time | Fits in 0.710 s airtime? |
|---|---|---|---|
| 180° reversal at jog | 10.80 m/s | 0.908 s | **no** |
| 180° reversal at sprint | 17.28 m/s | 1.452 s | **no** |
| 90° redirect at jog | 5.40 m/s per axis | 0.454 s | yes, using 64% of the flight |
| 90° redirect at sprint | 8.64 m/s per axis | 0.726 s | **no**, by 16 ms |

This is the shape the packet asks for: a controlled redirect at the apex is available and
costs most of the jump; a reversal is not available at any speed; and sprint commits harder
than jog, which is the same "speed is the dial" reading SKID-1 already uses.

---

## 3. Variable jump height

### 3.1 The model: release cut (shape a)

**Chosen: shape (a), release cut.** Full `JumpVelocity` on press; releasing while still
rising cuts the ascent.

Against "extremely tight", shape (b) — hold sustain — loses on two counts. It makes the
*press* weaker, since the launch impulse has to be reduced to leave room for the sustain,
and the press is the input the player is judging responsiveness by. And, as the packet
notes, it degrades under network jitter: shape (b)'s height is the integral of a stream of
held bits, so a dropped or late input tick costs height on the owning client that the
server does not lose, and reconciliation corrects it visibly. Shape (a) puts the whole
impulse on the one input that is already reliably delivered as an edge, and a late
*release* costs only the small height accrued in the extra ticks.

### 3.2 The implementation: a gravity multiplier, not a one-shot velocity multiply

```
public const float JumpReleaseGravityMultiplier = 3.0f;   // × Gravity = 66 m/s² while rising, released
```

The rising branch of `Step`'s gravity block becomes:

```
else if (!prev.Grounded)
{
    float g;
    if (velocity.Y < 0f)
        g = Gravity * FallGravityMultiplier;                        // falling: unchanged
    else if (velocity.Y > 0f && effective.JumpHeld && !locked && !ballistic)
        g = Gravity;                                                // rising, held: unchanged
    else if (velocity.Y > 0f && !locked && !ballistic)
        g = Gravity * JumpReleaseGravityMultiplier;                 // rising, released: the cut
    else
        g = Gravity;                                                // Y == 0, or locked/ballistic
    velocity.Y -= g * dt;
    fallSpeed = Mathf.Max(0f, -velocity.Y);
}
```

**Why a gravity term rather than a one-shot `velocity.Y *= 0.55f` on the release edge —
three independent reasons, each sufficient:**

1. **No new `MoveState` field, and therefore no snapshot layout change.** A one-shot cut
   needs a latch (`JumpCutArmed`) so it fires once and does not decay the whole ascent; a
   latch is a `MoveState` field; the snapshot flags byte has been full since v10, so it
   would be a second layout change on top of the input-side one. The gravity form is
   idempotent per tick by construction and needs no latch. The snapshot stays 55 bytes.
2. **Frame-rate independence.** A per-tick velocity multiply is the classic trap here: it
   applies once per *tick* rather than once per *second*, so it produces a different arc at
   a different tick rate and is not a pure function of `dt`. The gravity form is `g * dt`
   like everything else in the block. **MOVE-3b must not "simplify" this into a multiply.**
3. **MECHANICS §2, the flicker rule.** The cut is not a state, so there is nothing to enter
   or leave and nothing to chatter. A player mashing the key mid-ascent only swaps which
   gravity applies for that tick; energy is never gained (the released branch is always the
   harsher one), so the maximum apex is bounded at the held-throughout value no matter what
   the input does. A latched one-shot would have had to define what a re-press means, and
   that definition would have been a judgement call left to a coder.

**Boundary, stated per MECHANICS §1:** the cut condition is `velocity.Y > 0`, **strictly**.
At exactly `velocity.Y == 0` (the apex tick) ordinary `Gravity` applies, matching the
existing `velocity.Y < 0` fall test which is also strict. `velocity.Y` crosses zero once
per jump, monotonically, so the threshold cannot chatter.

**`!locked && !ballistic` is load-bearing, not defensive.** `locked` forces `JumpHeld`
false (§4.2), so without the explicit guard every control-locked or blast-launched rising
body would silently get 3× gravity — which would cut the eel blast's arc to a third of its
height and turn a shove into a stumble. With the guard, every locked and every ballistic
body gets exactly the gravity it gets today, bit-identical.

### 3.3 The resulting numbers

Continuous-math arithmetic, with the one-tick launch offset noted where it matters
(at 60 Hz the jump tick advances the body a full `8.4 × 0.0167 = 0.14 m` before any
gravity applies, because `prev.Grounded` is true on that tick and the gravity branch is
skipped entirely).

| | Held throughout (max) | Released immediately (min) |
|---|---|---|
| rising gravity | 22 m/s² | 66 m/s² |
| **apex above launch** | **1.60 m** | **0.67 m** |
| rise time | 0.382 s | 0.144 s |
| fall time back to launch height | 0.329 s | 0.213 s |
| **airtime** | **0.710 s** | **0.357 s** |

**Ratio: 2.4× in height, 2.0× in airtime.** The range is continuous and monotonic in
release time — release at time *t* into the rise gives apex
`0.14 + (8.4t − 11t²) + (8.4 − 22t)² / 132`, which is smooth from 0.67 m up to 1.60 m.

**Horizontal reach, flat lip to flat pad**, hold forward throughout:

| | full-hold jump (0.710 s) | min hop (0.357 s) |
|---|---|---|
| standing start | 1.02 m | 0.26 m |
| jog, 5.40 m/s | **3.83 m** | 1.93 m |
| sprint, 8.64 m/s | **6.13 m** | 3.08 m |
| sprint, input released in flight | 4.55 m | — |

**Practical clearance, not theoretical apex.** A ledge is cleared when the body is
descending onto it, so allow ~0.10 m of margin: a min hop reliably mounts **0.60 m**, a
full-hold jump reliably mounts **1.45 m**. §8's ledge bank is built on those two numbers.

---

## 4. The wire

### 4.1 `MoveIntent.JumpHeld`

Additive, init-only, defaults `false` — the exact shape `AimRaise` and `PolaroidShoot`
already use, so every existing construction site compiles unchanged.

```
/// <summary>Jump held this tick (level, not edge) — mirrors Sprint's shape. Consumed
/// only by the rising-release gravity cut in AvatarMotor.Step; it never fires a jump.
/// The edge <see cref="Jump"/> is still the sole trigger.</summary>
public bool JumpHeld { get; init; }
```

- **`IntentSources.cs`** (`LocalIntentSource`, beside the existing `Jump` line):
  `JumpHeld = Input.IsActionPressed("jump")`. No new input action; no `project.godot`
  change. On the tick a jump is pressed, `Input.IsActionJustPressed` and
  `Input.IsActionPressed` are both true, so a jump is always launched with `JumpHeld`
  true and the earliest a cut can register is the following tick — which is what makes
  0.67 m the true minimum.
- **The wander/bot intent source** leaves `JumpHeld` at its default `false`. Every bot hop
  is therefore a minimum hop. Deterministic, and stated here so nobody randomises it.

### 4.2 `AvatarMotor.Step` — the control-lock line

The existing `with { }` gains one field:

```
MoveIntent effective = locked
    ? intent with { MoveDir = Vector3.Zero, Jump = false, JumpHeld = false, Sprint = false }
    : intent;
```

Consistent with `Jump` and `Sprint`: a body that is not steering is not holding anything
either. §3.2's `!locked` guard means this zeroing is belt-and-braces rather than the only
thing standing between the eel kit and a cut arc.

### 4.3 `NetCodec` — a second buttons byte

**The existing buttons byte is full.** All eight bits were spent at v8 and `NetCodec` says
so in its own comment: *"The byte is now full. The next flag needs a second buttons byte
and therefore a real layout change, not another spare-bit reuse."* This is that flag.

- New field: `private const byte Flags2JumpHeld = 1;` in a new `buttons2` byte.
- `buttons2` is written **immediately after** the existing `buttons` byte in `PackInputs`
  and read in the same position in `UnpackInputs`, keeping the two a pure mirror.
- `InputEntryBytes` **25 → 26**.
- Bits 2, 4, 8, 16, 32, 64, 128 of `buttons2` are free; document them as such at the
  declaration so the next addition knows it does not need a third byte.
- Everything else in the layout keeps its order and its offsets relative to the new byte.
- **The snapshot layout is unchanged at 55 bytes**, by construction (§3.2 reason 1). Say
  so in the commit message; a reviewer will look for it.

### 4.4 `NetProfile.ProtocolVersion` — **11 → 12**

Mandatory, and it fails loudly on both sides, exactly as v11 did. `UnpackInputs`'s length
check is `packet.Length != 1 + count * InputEntryBytes`, so a v11 peer receiving a v12
input packet returns `null` outright rather than mis-parsing. Add a history entry
alongside the v11 note:

> **v12 (2026-08-26, MOVE-3 — variable jump height).** The second input-side length change
> in this list: `MoveIntent.JumpHeld` needs a bit and the buttons byte was documented full
> at v8, so the input entry gains a second buttons byte and goes 25 → 26 bytes. The
> snapshot is untouched at 55 bytes — the jump cut is a gravity term derived from the
> intent, deliberately, so no `MoveState` field and no snapshot bit were spent. The bump
> would be mandatory on meaning alone: a v11 peer would simulate every remote jump at full
> height while its owner cut it, which is a divergence rather than a cosmetic gap.

---

## 5. Parity and determinism

Everything specified here is a pure function of `(prev state, intent, dt)`.

| Term | Reads | Purity |
|---|---|---|
| air rate selection | `prev.Grounded` | already in `MoveState`, already replicated |
| wish-speed ceiling | `prev.Velocity`, `sf`, `waterMul` | all in-state or already derived in `Step` |
| jump cut gravity | `effective.JumpHeld`, `velocity.Y`, `locked`, `ballistic` | intent + this tick's derivation |
| landing | nothing new | §6 is the absence of a rule, not a rule |

- **No new `MoveState` field.** The one design pressure that would have added one — a
  jump-cut latch — was designed out (§3.2).
- **No `Input` reads inside `Step`.** The held bit arrives through `MoveIntent` like every
  other button, so the server, owner prediction and a reconciliation replay all see the
  identical value from the identical buffered entry.
- **No stored-on-the-client timer.** There is no new timer at all.
- **No frame-rate dependence.** Every term is `× dt`. §3.2 reason 2 names the specific
  trap that would break this.
- **Sanitation:** `JumpHeld` is a bool; there is nothing to clamp. A doctored packet can at
  most claim it held jump, which buys it a jump no taller than an honest client's — the
  ceiling is `JumpVelocity` either way, since the held bit can only *withhold* the extra
  gravity, never add energy.
- **Idempotency (MECHANICS §4):** the cut is re-derived every tick from the current
  velocity and the current bit. Applying it twice for the same tick is impossible, and
  replaying a tick produces the identical result.
- **Testability:** `RateFor(velocity, wish, grounded: false)` and the wish-speed ceiling
  should both be exercised in `dotnet test` without an engine, following the
  `ControlLockedBy` / `ShouldEnterSkid` precedent. The gravity selection is worth splitting
  into a pure `public static float GravityFor(float velocityY, bool jumpHeld, bool locked, bool ballistic)`
  for the same reason: it is a three-way branch with a boundary case at exactly zero, and
  that boundary should be provable in the fast tier.

---

## 6. Landing, and "dexterity to attempt tricks"

### 6.1 A landing preserves horizontal speed intact

```
public const float LandingSpeedMultiplier = 1.0f;   // there is no landing cost, deliberately
```

Named with its value so MOVE-3b transcribes an explicit decision rather than inventing a
number for a blank.

**Argued from the trick target, as the packet asks.** A hop chain is the substrate for
every emergent trick worth having, and a hop chain is exactly the case where a landing cost
compounds: at a 10% cost, five pads leave you at 59% of your entry speed, so a chain the
player cleared at hop two they fall short of at hop five, for reasons they cannot see and
cannot correct. That converts a skill expression into a decay curve. The player who wants
to shed speed on landing already has a way to do it — release the stick, and
`Deceleration = 21` takes over the moment `Grounded` returns.

Two further reasons, both structural:

- **The game already charges for momentum, once, in the right place.** SKID-1's turnaround
  is the cost of a *badly committed* direction change, and it is chosen by the player. An
  unavoidable landing tax would charge a second time for the same thing and would make the
  skid's "speed is the dial" reading incoherent.
- **The landing already reads as costly without being costly.** `StepEvents.FallSpeed` is
  already reported for exactly this, and the presentation layer already scales a landing
  squash from it. Selling the impact is the presentation layer's job; taking the speed is
  not.

**What resumes on the landing tick, unchanged:** full ground rates via
`RateFor(…, prev.Grounded)` from the tick after touchdown; `Deceleration = 21` if input was
released; and the skid, which is *kept* — `ShouldEnterSkid` requires `grounded`, so a
player who lands at ≥ 4.05 m/s while holding a reversed direction enters a turnaround skid
on the landing tick. That is a good emergent trick (land-and-slide), it costs nothing to
keep, and MOVE-3b should not gate it.

### 6.2 A buffered jump off a landing chains cleanly — and already does

**No change required. Verified against the shipped code, not assumed.** On the tick after
touchdown, `prev.Grounded` is true, so `coyote = CoyoteTimeSec` is refreshed and a live
`jumpBuffer` fires the jump immediately; the gravity branch is skipped that tick, so the
launch is a clean full `JumpVelocity = 8.4`. **The chain costs exactly one tick — 16.7 ms —
after touchdown.** That is as tight as a fixed-tick simulation can be, and it is the reason
§8's hop chains are testable at all.

Two consequences, both wanted, both stated so they are not "fixed":

- **A buffered *tap* fires as a minimum hop.** The buffer is set by the press edge; by the
  time it fires, the key is released, so §3.2's cut applies from the first airborne tick.
  Tap-tap-tap gives a low fast chain, hold-hold-hold gives a tall slow one. That spread is
  free and it is the substrate for two different chain rhythms — §8 builds a course for
  each.
- **Holding jump does not auto-bounce.** `jumpBuffer` is armed only by the edge
  (`effective.Jump`), so a player who holds the key through a landing gets nothing; a jump
  requires a fresh press. This is deliberate: an auto-bounce makes the height of every hop
  in a held chain a function of *when* the player happened to be holding, which is the
  opposite of tight, and it removes the tap/hold distinction above. Stated because "holding
  jump should keep bouncing" is a plausible thing for a coder to add unasked.

### 6.3 Emergent, not authored

The deliverable is a body precise enough for a player to invent with. The five things the
numbers above make reliably possible, and which §8 exists to let Talon try:

1. **Reliable gap jumps** at three legible distances (1.02 / 3.83 / 6.13 m), the width
   chosen by which gear you arrive in.
2. **Momentum-preserving hop chains**, in two rhythms (tap and hold), guaranteed by §6.1
   and §6.2.
3. **A controlled redirect at the apex** — 90° at jog costs 64% of the flight; the same
   move at sprint does not fit, so the redirect has a price in commitment.
4. **A landing you can plan**, because the arc is a closed-form function of release time
   and nothing about touchdown is stochastic.
5. **Land-and-slide**, free, from the existing skid meeting a preserved landing speed.

### 6.4 Deferred — needs Talon, not specced here

Named because the emergent set obviously wants them, and deliberately left as one line
each. **None of these is designed, costed, or implied by anything above.**

- **A double jump / air hop.** The single most-requested addition to any body with this
  arc, and the one that most changes what §8's gap widths mean.
- **A dive or roll.** A committed forward burst that trades control for reach; it would
  need its own input, its own recovery window, and a control-denial decision.
- **A mantle / ledge grab.** Would convert the 1.80 m "honest wall" in §8 from a hard limit
  into a slower route, which is a different game.
- **A long jump (sprint + jump + a modifier).** Would want a horizontal impulse, which is
  the one thing §2.3's ceiling is built to forbid.

---

## 7. Interactions — one line each

- **`ballistic` (eel blast, `EelKitFlag` off by default).** The horizontal block's
  `if (ballistic) { }` branch is untouched and still runs first, so `RateFor` is never
  consulted for a launched body and the air fractions cannot double-apply on top of it;
  the jump cut is additionally guarded by `!ballistic` (§3.2) so the launch arc keeps
  `Gravity = 22` exactly as it does today.
- **Control lock (sputter-out, incapacitation, impulse ragdoll — the `ControlLockedBy`
  union).** `JumpHeld` is zeroed in the same `with { }` as `Jump` and `Sprint`, and the cut
  carries an explicit `!locked` guard, so every locked body's arc is bit-identical to
  today's.
- **Water — swimming.** The `if (swimming)` arm overwrites `velocity.Y` and is mutually
  exclusive with the gravity branch the cut lives in, so a swimming body can never be cut;
  `WaterGeometry.JumpAllowed` already denies the jump itself.
- **Water — wading.** Air control applies normally, and because §2.3's ceiling is built
  from `groundWish` (already `× waterMul`), a hop out of the shallows cannot exceed wading
  speed in the air.
- **The turnaround skid.** `ShouldEnterSkid` already refuses while airborne, so the air
  rates and the skid can never both apply; a landing into a reversed input enters a skid on
  the landing tick and that is kept deliberately (§6.1).
- **Coyote time and jump buffer, confirmed at 0.12 s each.** Both sit mid-window of Talon's
  0.08–0.16 s, and the arithmetic supports them: 0.12 s is 7.2 ticks of buffer at 60 Hz,
  and 0.12 s of coyote is 1.04 m of forgiveness past a lip at sprint, 0.65 m at jog. No
  change. A coyote jump taken at the very end of the window launches from 0.16 m below the
  lip and therefore travels *further*, not shorter — so nothing in §8's geometry has to
  budget against a late jump falling short.
- **`LocomotionProfile`.** Out of packet and untouched; see §9 for the one thing this
  change makes visible there.

---

## 8. The movement playground — element list with dimensions

**This is a dev harness, not a level.** It is stated explicitly rather than left silent:
`LEVEL-BIBLE.md` governs composition — zones, spawns, paths, pacing, extraction, ambient
legibility — and a playground has none of those as design objects. It has no threats, no
pressure driver, no return, no atmosphere. Nothing in this section is a level design
decision and nothing in it should be carried into one.

**Global:** flat, hard-surfaced, no water anywhere (keeps `JumpAllowed`/`SprintAllowed`
out of the test), flat neutral lighting, a fixed spawn on the plaza, and a soft recovery
floor **2.0 m** below every raised element so a failure costs two seconds and not a
reload. Total footprint about **60 × 60 m**. Every raised platform gets a **0.1 m** lip
colour change so the takeoff point is unambiguous.

Every dimension below is derived from §2 and §3.3.

### 8.1 Plaza — ground feel

- **30 × 30 m** flat. Enough to reach sprint (4.15 m of ramp) in any direction and stop.
- **Sprint lane:** 24 m straight, marked at 1.62 m (jog reached) and 4.15 m (sprint
  reached) from the start line.
- **Skid lane:** the same 24 m run-up ending at a line, with marks every 1 m for 6 m past
  it. A sprint reversal covers **2.82 m**; a jog reversal covers **1.06 m**. Existing
  behaviour, included so a regression is visible.

### 8.2 Ledge bank — brackets min and max apex

Five blocks, each **3 m** wide, side by side off a common flat approach, approached at jog.

| Height | What it tests |
|---|---|
| **0.40 m** | a tap clears it easily — the curb |
| **0.60 m** | the tap's practical ceiling (min apex 0.67 m, 0.07 m of margin) |
| **1.00 m** | needs a partly-held jump — the middle of the range must be usable |
| **1.45 m** | needs a fully held jump (max apex 1.60 m, 0.15 m of margin) |
| **1.80 m** | impossible — the honest wall, so the ceiling is legible rather than mysterious |

### 8.3 Gap bank — brackets max jump distance

Five parallel lanes, each with its own **≥ 12 m** run-up (enough for sprint plus settling),
takeoff lip and landing pad flush at the same height, **1.25 m** above the recovery floor.
Landing pads **3.0 m** deep.

| Clear gap | Verdict |
|---|---|
| **1.6 m** | a standing jump (1.02 m) fails; a jog jump clears. "You must be moving." |
| **3.4 m** | a jog jump clears with 0.43 m of margin |
| **4.2 m** | a jog jump fails; a sprint jump clears comfortably |
| **5.8 m** | only a sprint jump clears (6.13 m, 0.33 m margin) — the precision gap |
| **6.6 m** | impossible — the honest wall |

### 8.4 Air-brake A/B — the specific thing being fixed

A **24 m** sprint lane to a lip at **1.25 m**, then two landing pads at the same height:

- **Pad A**, near edge **1.6 m** past the lip, depth 1.0 m.
- **Pad B**, near edge **4.4 m** past the lip, depth **2.2 m** (covers 4.4–6.6 m).

A sprint jump lands at **6.13 m** holding forward and at **4.55 m** with input released for
the whole flight — both on pad B. **Pad A is unreachable.** Under today's 100% air control
you can brake to a stop mid-air and drop onto pad A; under this spec you cannot. It is the
one element whose *before* state is worth capturing.

### 8.5 Redirect pad — air control, measured

A lip at jog approach, with three pads at the same height, each **1.5 m** deep:

- **Straight:** 3.8 m forward, 0 lateral — the control case.
- **Reachable redirect:** **2.9 m forward, 1.5 m lateral.** Computed: a diagonal wish from a
  jog takeoff blends to 6.35 m/s², giving 1.56 m of lateral and 2.91 m of forward travel in
  the 0.710 s flight.
- **Unreachable redirect:** **2.9 m forward, 3.0 m lateral.** The ceiling made visible.

### 8.6 Coyote ledge

A **12 m** run-up to a platform at **1.25 m** whose edge is followed by a **3.4 m** gap to a
same-height pad, with a **3.0 m** drop between them. A floor marking **0.65 m** past the lip
shows where the coyote window closes at jog (1.04 m at sprint — mark both). Without coyote,
a jump pressed after the lip does not fire at all and the player falls; with it, the jump
fires and clears with margin.

### 8.7 Buffer pad

A **2.0 m** drop onto a landing pad only **1.0 m** deep (0.185 s of ground contact at jog),
immediately followed by a **3.4 m** gap. Clearing it requires the jump to be pressed inside
the 0.12 s before touchdown or the 0.185 s on the pad — that is the buffer and the
one-tick chain of §6.2 being the only thing that makes it possible.

### 8.8 Hop chains — two rhythms, and the landing-cost test

Both at **1.25 m** height over the recovery floor, pads **1.2 m** wide.

- **Hold chain:** 5 pads, **2.0 m** deep, centres **5.2 m** apart (3.2 m edge-to-edge). A
  full-hold jog jump from the far edge lands 0.63 m onto the next pad. A tap (1.93 m) falls
  short, so the chain requires held jumps.
- **Tap chain:** 5 pads, **1.5 m** deep, centres **3.0 m** apart (1.5 m edge-to-edge).
  Clearable with minimum hops at jog.

**Both chains are the landing-cost test.** With `LandingSpeedMultiplier = 1.0` the fifth
hop is identical to the first. If MOVE-3b introduces any landing friction, the hold chain
fails at pad four or five and the failure is visible without instrumentation.

### 8.9 Precision target and stairs

- **Precision target:** a lip at jog approach and a **1.0 × 1.0 m** pad with its near edge
  at **3.4 m**, so its centre sits at 3.9 m against the 3.83 m jog jump.
- **Walkable stairs:** 5 steps, **0.25 m** rise × **0.60 m** run, rising to 1.25 m — under
  the min hop, so it must be climbable without ever pressing jump. This is a regression
  guard: stairs that fight the motor are the classic way a jump change breaks traversal.
- **Jump stairs:** 3 steps, **0.75 m** rise × **1.20 m** run — above the min hop's 0.60 m
  practical clearance and below the full jump's 1.45 m, so each step requires a held jump.

---

## 9. Flagged, not changed

Findings from this analysis that touch constants or behaviour outside this packet's scope.
**None of them is being changed here.**

1. **`Deceleration = 21` is pinned by the eel kit, not by feel.** `EelProfile.SkidSec` is
   `LaunchSpeedFullMps / Deceleration` and `EelProfile` invariant 1 puts a hard floor at
   18.4 m/s². The new `AirControlBrake` must never be routed into that derivation; if
   `EelProfileTests.Invariant1` ever fails after MOVE-3b, the air brake has been wired into
   the launch path and the fix is there, never in the test.
2. **`MoveToward` is applied per axis, so a diagonal change resolves up to √2 faster than an
   axis-aligned one.** Pre-existing on the ground; the air fractions inherit it unchanged.
   §2.5 and §8.5's numbers are computed against the shipped per-axis behaviour, not against
   a magnitude-clamped ideal. Changing it is a MOVE-1 retune and is out of scope.
3. **`Sprint` is honoured airborne with no `Grounded` gate.** §2.3's ceiling neutralises its
   effect rather than adding a gate, deliberately: `LocomotionProfile` and the presentation
   layer read the sprint bit, and gating it in the motor would change what they see.
4. **There is no terminal velocity.** Nothing in the playground falls far enough to matter
   (a 2.0 m recovery drop arrives at 10.9 m/s), and adding one is a new constant nobody
   asked for.
5. **`LocomotionProfile` has no airborne gear.** The gait derives cadence and stride from
   ground speed with no `Grounded` term, so an airborne body currently runs its legs
   through the whole flight. Longer airtimes make that more visible, not less. It is a real
   presentation finding and it belongs to whoever owns `LocomotionProfile` next; it is not
   MOVE-3b's to fix and it does not block anything here.
6. **`StepEvents` has no `Landed` fact.** The presentation layer infers a landing from
   `FallSpeed` plus a grounded transition. Fine today; if MOVE-3b wants a landing cue for
   the playground it should add the event rather than re-deriving the transition in a
   second place.

---

## 10. Bible check

```
Bibles applied:  MECHANICS (the jump's boundary at velocity.Y == 0, the flicker rule at the
                 cut threshold, idempotency of a replayed tick, the extremes of the speed
                 ceiling); BEHAVIOR (movement rates, facing unchanged in the air);
                 INTERACTION (the held-jump bit is a new input contract — tap vs hold, and
                 what a press acknowledges). LEVEL deliberately NOT applied: §8 is a dev
                 harness, not a level — no zones, spawns, pacing, extraction or ambient
                 legibility are being designed, and nothing in §8 should be carried into a
                 level. THRILL not invoked: this packet specifies a control surface, not an
                 affect; if the playground later needs to make anyone feel anything, that
                 goes through /direct.
Items checked:   MECHANICS §1 (inclusive/exclusive bounds — §3.2's strict velocity.Y > 0 and
                 the exactly-zero apex tick), §2 (state transition integrity — the cut is
                 deliberately NOT a state, so there is nothing to enter, leave or flicker;
                 §7 states every interaction with the four existing control-denial states),
                 §3 (race order — the cut resolves inside the existing gravity branch, before
                 the jump block, one fixed order on every peer), §4 (idempotency — the
                 gravity form is re-derivable per tick and a replay reproduces it exactly),
                 §6 (numeric rules at the extremes — §2.3's table walks zero speed, max
                 speed and the ratchet-down case). BEHAVIOR §2 (orientation — facing is
                 unchanged in the air, stated out loud so "moves without turning" is not
                 read as the shipped backwards-chase bug), §3 (movement values as
                 per-context numbers rather than one global rate — three air fractions, one
                 per job). INTERACTION §2 (feedback at the moment of trigger — the press
                 always launches at full JumpVelocity so the input is acknowledged before
                 any cut can apply), §4 (tap vs hold pinned rather than inherited — §3.3's
                 0.67 m / 1.60 m spread and §6.2's tap-vs-hold chain), §6 (reversibility —
                 §6.2 states that holding jump does not auto-bounce and a jump requires a
                 fresh press), §7 (interrupt handling — §7's six lines are exactly "what
                 happens if this is interrupted by each control-denial state").
Result:          Pass. Two things were changed BY the check rather than found passing:
                 (1) the jump-cut model moved from a latched one-shot velocity multiply to a
                 gravity multiplier, because MECHANICS §2 and §4 made the latch's re-press
                 semantics an undefined state transition and a per-tick multiply
                 frame-rate-dependent — the gravity form has no state to flicker and is
                 pure in dt; (2) the cut gained explicit !locked && !ballistic guards after
                 §7's interaction pass showed that zeroing JumpHeld under a control lock
                 would have applied 3x gravity to the eel blast's rise and to every shoved
                 body, cutting arcs that must stay bit-identical.
```
