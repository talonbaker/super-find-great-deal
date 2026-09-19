# The mounted moveset — audit and options — BIKE-3B

**Date:** 2026-09-02
**Packet:** BIKE-3B (systems-design)
**Branch:** `docs/2026-09-02-bike-3b-mounted-design` (base `8bf0c2a9`)
**Companion:** `docs/design/2026-09-02-bike-rider-animation-states.md` (the rider's body,
same packet). BIKE-3A owns the bike mesh's orientation; BIKE-3C owns what Wobble can lend
the handling. Nothing here restates either.
**Status per the commitment ladder (DESIGN-BIBLE):** the audit (§1) is *measurement* — read
from the code at `8bf0c2a9`, not a proposal. The options (§2) are `exploring`. The
recommendation (§3) is `provisional` — a recommendation, not a verdict; both forks in §4–§5
are Talon's.

**Audit rule used throughout:** the code is the truth; a report is provenance, not a ruling.
A row is **RULED** only where Talon's own words decide it (quoted, dated, with the file that
records them). A row is **LEAKED** where nothing rules it — including rows an agent decided
deliberately and recorded in a report Talon received but never answered; those say so in the
provenance column, because "written down near him" and "ruled by him" must not blur.

---

## 0. The question, and the one-paragraph answer

Talon (2026-09-02, verbatim in the BIKE-3B packet): *"it's unclear whether reusing the
running system is fundamentally the wrong approach or just needs to be adapted/restricted for
bike state."*

**Reuse is not fundamentally wrong. Three separable defects are stacked on top of it, and
only one of them indicts the current carrier.** (1) The inheritance is *unscoped* — the ride
never says which on-foot verbs it keeps, so the crouch grammar, the turnaround skid and the
jump chain all fire at ride speed whether anyone meant them to (§1). (2) The riding is
*invisible* — the rider's legs run the on-foot gait through the frame, so even the verbs
Talon asked for by name read as "the running system carried over" (the companion doc's
subject). (3) The carrier — rewriting the process-wide `MotorTuning.Current` — *cannot ship
multiplayer*, because one static tuning cannot make one avatar ride while five run
(`AvatarMotor` reads `MotorTuning.Current` for every body in the process:
`scripts/net/AvatarMotor.cs:68` onward). Fix (1) with an explicit mounted verb-mask, fix (2)
with the rider pose channel, and the reuse itself is load-bearing and proven — Talon rode it
for 80.1 % of his first session (`2026-09-02-BIKE-2x-telemetry-read.md`), and the
mount/burst/dismount chain composes without a case table (BIKE-1b report). Fix (3) is a
choice of shape, and that is §2.

---

## 1. The audit — every on-foot ability, what it does while mounted, and who ruled it

All line numbers are at `8bf0c2a9`. "The layer" is
`scripts/dev/playground/BikeLayer.cs`; "the motor" is `scripts/net/AvatarMotor.cs`;
"the ride rewrite" is `BikeRig.Ride` (`scripts/dev/playground/BikeRig.cs:27-49`), which
touches **only** MoveSpeed, Acceleration, Deceleration, the two turn rows, the three
air-control rows, JumpVelocity and AirJumpVelocityFraction — every other row of the
58-row tuning rides across untouched, which is the mechanism behind most LEAKED rows below.

| # | On-foot ability | While mounted, today | Code | Mark | Provenance |
|---|---|---|---|---|---|
| 1 | **Jump** (SPACE edge, grounded) | The bunny-hop: the motor's own jump at `JumpVelocity × RideJumpMul` (1.0 — same height as on foot) | `BikeRig.cs:45`; row `BikeTuning.RideJumpMul` (`BikeTuning.cs:76`) | **RULED** | Talon 2026-09-01: *"I would like to see a double jump on a bike"* — a second jump requires a first (`roles/programming/inbox/2026-09-01-BIKE-1b-presets-park-double-jumps.md`). **Contradicted 2026-09-02 — see §4.** |
| 2 | **Air jump** (SPACE, airborne) | The double jump ON the bike: `AirJumpVelocityFraction × RideAirJumpMul` | `BikeRig.cs:46-47`; `BikeTuning.cs:83` | **RULED** | Same verbatim ask: *"a double jump on a bike but also off the bike."* Built as BIKE-1b, measured 6.72 m/s in-engine (BIKE-1b report). **Contradicted 2026-09-02 — see §4.** |
| 3 | **Sprint** (SHIFT held) | Still scales the wish: ride cap = `MoveSpeed × RideSpeedMul × SprintMultiplier` (9.42 m/s at shipped numbers) | `BikeRig.RideCapMps` (`BikeRig.cs:92-93`); `Ride()` never touches `SprintMultiplier` | **LEAKED** | Deliberate agent decision, recorded ("sprint multiplier… rides across untouched", BIKE-0 report §grammar) — never ruled. A bike with a sprint *button* is a real design question nobody has asked. |
| 4 | **Hold-to-sprint ramp** (H) | Applies identically mounted — the layer scales `MoveDir` by held-time with no `Mounted` gate | `BikeLayer.cs:299-305` | **LEAKED** | Built to answer the on-foot "no sprint button" question (BIKE-0); reaches the bike by omission. |
| 5 | **Crouch grammar — Tuck / Slide / DuckWalk** (jump button held ≥ 0.20 s while grounded) | **Fully reachable mounted.** Hold SPACE 0.20 s on the ground and the motor enters SLIDE at ride speed (or TUCK at rest); nothing in the layer masks it | Entry: `AvatarMotor.StepVerb` T10/T11 (`AvatarMotor.cs:1698-1707`); window `JumpHoldWindowSec = 0.20` (`MotorTuning.cs:410`); the layer's `NextIntent` passes `Jump`/`JumpHeld` through untouched while mounted | **LEAKED** — and as of 2026-09-02 **ruled OUT** (*"…it shouldn't have, specifically jump and slide"*) with no earlier ruling opposing, so the mask in every §2 option removes it. **This is the likeliest "slide on the bike" Talon hit — see §1.1.** |
| 6 | **Slide-jump overlay** (SPACE out of a slide, ×1.35) | Does **not** fire mounted — explicitly gated `!Mounted` | `BikeLayer.cs:288` | **LEAKED** (in the direction of absence) | Nothing rules its absence either — the gate is an agent decision (BIKE-0). Kept as the counterexample: masking is cheap and the layer already knows how. |
| 7 | **Turnaround skid** (reverse the stick at speed) | Fires mounted at ride speed: the skid rows ride across, so a 9.4 m/s reversal is a 13 m/s² skid-brake with the skid pose, on the bike | `MoveState.SkidRemaining` / `AvatarMotor.StepSkid`; `Ride()` touches no `Skid*` row | **LEAKED** | Never mentioned in any bike ruling. Note before killing it: it is accidentally the brake the bike otherwise lacks (RMB is the drift, deliberately not a brake — BIKE-0 answers). Keep/kill belongs to the verb-mask definition, flagged in §2. |
| 8 | **Chain jump** (consecutive hops raise the wish) | Accrues on bunny-hops: `ChainDepth` logic is untouched, so chained hops raise the *ride* wish | `MoveState.ChainDepth`; `AvatarMotor.StepChain`; `Ride()` touches no chain row | **LEAKED** | Chain × RideSpeedMul compounds two speed systems nobody priced together. |
| 9 | **Swing / attack** (LMB) | Refused outright while mounted | `BikeLayer.OnSwingPress` (`BikeLayer.cs:457`) | **RULED** | Talon 2026-09-01: the attack is not on the bike (BIKE-1b report, BIKE-1c addendum table). |
| 10 | **Aim** (RMB shipped meaning) | Suppressed always in the lab; mounted RMB is the drift | `BikeLayer.cs:277-284` | **RULED** (mounted half) | Talon 2026-09-01: the bike's right mouse is *"a drifting slide… not a brake"* (BIKE-0 breakdown, "Talon's answers"). The on-foot RMB=crouch half is lab plumbing, not a ruling. |
| 11 | **Coyote time / jump buffer** | Ride across untouched — same forgiveness on and off the bike | `Ride()` touches neither row | **LEAKED** | Benign leak; recorded so the row is not unmarked. |
| 12 | **Interact / Throw** (E / Q-as-throw) | Pass through the layer untouched; moot in the lab (no props), live in the shipped game | `BikeLayer.NextIntent` never reads either bit | **LEAKED** | Except the key itself: Talon ruled *"Throw moves if the bike ships"* (BIKE-0 breakdown answer 1). What Interact *does* while riding — grab a prop from the saddle? — is undesigned. |
| 13 | **Water** (riding into the lake) | No bike rule exists. The motor's water fold takes over the body; `Mounted` stays true, the ride tuning stays applied, the bike stays under a swimming body | Water dominates verbs at `StepVerb` T3 (`AvatarMotor.cs:1642`); no water branch anywhere in `BikeLayer` | **LEAKED** | Unhandled state, the "some system was never told" shape (MECHANICS-BIBLE §2). The verb-mask spec must say: water force-dismounts (companion doc §7). |
| 14 | **Incapacity / control lock / impulse ragdoll** | The motor zeroes steering (T1 dominates), but the layer never dismounts: a knocked-out body keeps `Mounted = true` and the ride tuning | `AvatarMotor.cs:615-626`; no incapacity read in `BikeLayer` | **LEAKED** | Same class as row 13: a state change whose dependents were not updated together (MECHANICS-BIBLE §2). Force-dismount on any control lock. |

Bike-native verbs (mount/dismount/kick-off/landing window, stumble, drift, ramp launch,
step-up, swing-slingshot) are not "inherited" and are out of this table's scope; their
rulings are recorded in the BIKE-0/1b/2x reports.

### 1.1 The "slide on the bike" — traced, not guessed

Mounted RMB is the drift, so the slide Talon hit did not come from the slide button. Three
routes exist in code, in descending likelihood:

1. **Held SPACE while mounted** (row 5). The habit the lab itself teaches — hold SPACE for a
   full-height arc (`JumpHeld` suppresses the release-gravity cut) — becomes a crouch trigger
   the moment the wheels touch down: touchdown with the button still down starts the 0.20 s
   window (`StepVerb` T8, `AvatarMotor.cs:1650-1659`), and twelve ticks later the body is in
   SLIDE at ride speed, folding into the slide pose while the bike is still under it. No
   press "asked" for it.
2. **Dismount into slide.** On foot during the dismount blend, RMB presses the crouch
   grammar (`BikeLayer.cs:283`) — a slide entered at near-ride speed while the ride tuning
   is still blending out, with the ×1.35 slide-jump overlay live again (`BikeLayer.cs:288`
   gates on `!Mounted`, and the body *is* dismounted). Legitimate on-foot behaviour that
   *reads* as a bike ability because the speed came from the bike.
3. **The stumble misread.** A rolling dismount over the foot cap clamps and stumbles
   (`BikeLayer.Dismount`, `BikeLayer.cs:546-556`) — a lurch that can read as an uncommanded
   slide, especially with no stumble pose on the body (none exists; BIKE-0 open question 4).

Route 1 is the only one that happens *while mounted*, matching the field note's wording.

---

## 2. The mechanical shape — three options

Common to all three (not at issue): the **mounted verb-mask** — an explicit, written list of
which verbs exist while mounted, with everything else masked, replacing today's
inherit-by-omission. The audit is its first draft: rows 5, 13, 14 masked or force-dismounted
under every option; rows 3, 4, 7, 8, 12 get explicit keep/kill entries when the mask is
frozen (values Talon's, per the tuning-surface convention); rows 1–2 wait on §4.

### Option A — keep the tuning-rewrite layer, add the mask in the layer

**What it is.** `BikeLayer` stays the shape it is: an intent decorator plus a post-step
writer, rewriting `MotorTuning.Current` through the single writer (`MotorTuning.TryApply`).
The mask is applied on the *input side*: while `Mounted`, the layer strips/holds the intent
bits that reach masked verbs (e.g. never lets a grounded `JumpHeld` accrue the crouch
window), exactly as it already suppresses `AimRaise` and already gates the slide-jump.

**Net story, in one sentence:** the mask edits the controller's message *before it is sent*,
so the server and the player's own prediction both see the same already-masked buttons and
cannot disagree about them — but the tuning-rewrite half stays a process-wide static
(`MotorTuning.Current` is one value for every avatar in the process), **so option A can
never make one avatar ride while another runs on the same server: it is a lab and
single-player shape only.**

**Cost line:** roughly a day in the lab, zero shipped files, keeps every preset and the
whole self-test harness working unchanged; known dead end for multiplayer — it must grow
into B (and is B's rehearsal: the mask written for A is the same mask B compiles into the
verb table).

### Option B — the mounted verb set inside the motor's state machine

**What it is.** The mount becomes simulation state, the way the crouch verbs did in MOVE-5.
`MoveState` grows:

- `Mounted` — 1 bit;
- `MountBlendTicks` — a byte, integer-by-construction exactly as `VerbClockTicks` argues for
  itself (`MoveState.cs:169-188`), counting the mount/dismount blend so every peer derives
  the same blend fraction;
- one air-budget bit or a widened `AirJumpsUsed` (which shape depends on §4's ruling);
- `DismountArmedTicks` — a byte, only if the predicted landing-dismount cast (BIKE-1b's one
  real design move) survives into the shipped grammar.

`AvatarMotor.Step` derives the ride tuning per-avatar at the top of the tick —
`BikeRig.Ride(foot, bike, blend)` is already pure and already "in the form the motor's
`Step*` helpers take" (its own file says this was the plan: `BikeRig.cs:8-14`). `StepVerb`
gains a mounted rule beside T2/T3 (mounted forbids crouch entry — a data shape, not a check,
per the Law V0 pattern), and the bursts become step resolutions like `StepJump`.

**Net story, in one sentence:** the mount lives in the same replicated state the crouch
verbs and the skid already live in, so the server, the rider's own prediction and every
other player's view of the rider all read one fact, reconciliation replays it correctly by
construction, and "clean, immediate switching" costs zero round-trips — the mount is
predicted like a jump, not requested like a purchase.

**Cost line:** the real feature cost, already scoped by the parked BIKE-3 row of the program
breakdown: shipped motor + `NetCodec` (both snapshot flag bytes are documented full, so this
is a new byte and a protocol bump), a `STATE-CASCADE-TABLE.md` pass for the mount state
change, and the full two-command suite. Nothing about it is speculative — every mechanism it
needs (replicated verb, tick-counter clock, budget bits, proxy adoption) shipped in MOVE-5
and is the pattern `MoveState.AdoptForProxy` exists to absorb.

### Option C — a separate mounted motor (steelmanned, then costed)

**Steelman.** A bike is not a biped. A dedicated `BikeMotor.Step(in BikeState, …)` could
carry bike-native state the foot motor has no rows for — lean coupled into steering,
wheelbase pitch, crank position, a real countersteer read (BIKE-3C's subject) — without
dragging 58 foot rows that mostly do not apply, and with the signed-off on-foot feel
provably untouched (zero regression surface: the foot motor's file never changes). Wobble is
the precedent that a bespoke bike model can ship and feel right. Verb scoping is free: a
machine that never had a crouch cannot leak one.

**Net story, in one sentence:** every mount and dismount becomes a handover between two
separately-predicted machines, so the laggy moments — a mount pressed just before a
correction arrives — are the moments where the two machines can briefly disagree about
which one owns the body, and that disagreement lands exactly on the transition Talon wants
seamless.

**What it costs.** A second predicted state machine is a second reconciliation path, a
second codec block, a second replay-determinism suite, and a second tuning table — and the
mount/dismount becomes a *cross-machine handover*, which parks the seam exactly where
Talon's stated top priority lives (*"clean, immediate switching between running and mounted
bike state"*): every transition must translate state both ways, mid-air, mid-blend, under
packet loss, and a translated state is a state that can disagree. The lab already rejected
the adjacent shape once, with reasons that still hold: a copied motor is "2,100 lines that
drift from the shipped motor the day after they are copied" (BIKE-0 report, Decisions).
And it argues against Talon's own standing ruling on what the bike *is*: *"an extension of
walking and running and jumping"* (2026-09-01) — an extension shares an implementation; a
vehicle you enter gets its own. Choose C only if BIKE-3C concludes that bike-native physics
the tuning-rewrite genuinely cannot express (e.g. lean-as-simulation with countersteer) is a
requirement — that finding is C's only trigger, and it has not been made.

---

## 3. Recommendation, ranked against the tenets

**A now, B when the bike is ruled in, C only on BIKE-3C's named trigger.** The lab keeps
iterating feel this week at option-A cost; the mask written for A is B's verb-table rules;
B is the only shape that ships the stated networking requirement. A recommendation is not a
verdict; the fork is Talon's (§5).

| Tenet (order per canon §0.4) | A (layer + mask) | B (motor-internal) | C (second motor) |
|---|---|---|---|
| Performance | Best today — zero shipped-path cost | Equal at ship — one derivation per tick, pure | Fine — one machine steps at a time |
| Reliability / safety | Fine offline; **cannot be made correct in MP** (global tuning) | Best — one replicated fact, one state machine, replay-correct by construction | Worst — doubled desync surface, translated handover at the highest-priority seam |
| Modern industry standards | Standard prototyping shape | Standard for predicted movement (verb-in-state, the MOVE-5 pattern) | Standard for *enterable vehicles*; the bike is ruled an extension, not a vehicle |
| Simplicity (tiebreaker) | Simplest today | One machine, one table | Two of everything |

---

## 4. FORK — the jump contradiction (Talon's; surfaced ripe, not resolved)

**Trigger:** the mounted verb-mask cannot be frozen — under *any* §2 option — until this is
ruled, because the mask must say what SPACE does while mounted. Everything else in this
document proceeds without it.

**Ruling 1 — Talon, 2026-09-01, verbatim** (recorded in
`docs/agents/roles/programming/inbox/2026-09-01-BIKE-1b-presets-park-double-jumps.md`; built
as BIKE-1b, commit `62f92179`):

> "Also I would like to see a double jump on a bike but also off the bike. Also jump off
> with the tolerance window."

**Ruling 2 — Talon, 2026-09-02, verbatim** (the BIKE-3B packet, ISSUE 3):

> "the bike has inherited on-foot movement abilities it shouldn't have, specifically jump
> and slide, straight from the on-foot state machine."

These cannot both stand as written: the bike's jumps are not accidental inheritance — they
are tuned multiples built to the 2026-09-01 ask (`RideJumpMul`, `RideAirJumpMul`), and the
kick-off/landing-dismount chain was built on top of them the same day. The slide half of
ruling 2 stands unopposed (nothing ever ruled a slide in — audit row 5) and is acted on in
every option. The jump half is the contradiction.

**A reading that may dissolve it, offered without being chosen:** what the field showed was
running legs at ride speed plus an unasked-for slide — the tuned jumps are mechanically
present but *visually indistinguishable* from the running system, because nothing pedals,
nothing straddles, and a bunny-hop looks exactly like a running jump. Ruling 2 may be an
observation about that presentation rather than a repeal of ruling 1. Only Talon knows.

**The options, and what each does to the kick-off/landing-dismount chain** (the chain today:
SPACE bunny-hop → SPACE bike double jump → Q kick-off is three rises; mount burst → landing
dismount → jump → re-mount is the four-press chain BIKE-1b built):

| | SPACE grounded | SPACE airborne | Q chain | What dies | What it says |
|---|---|---|---|---|---|
| **(a) Keep the chain as built** | bunny-hop | bike double jump | intact — three rises | nothing; the field complaint must then be fully answered by the slide mask + the rider animation (companion doc) | ruling 2's jump half read as presentation |
| **(b) Strip SPACE mounted entirely** | nothing | nothing | **kick-off and landing dismount survive** (both are Q verbs and never used SPACE); the air chain shrinks to mount-burst → kick-off (two rises) | the bunny-hop (curbs fall back to step-up + hop-on — BIKE-1b question 5's other branch), the bike double jump, `RideJumpMul`/`RideAirJumpMul`, two self-test checks | the bike is ground-committed; partially walks back the 2026-09-01 *"extension of… jumping"* ruling |
| **(c) Keep the hop, strip the air jump** | bunny-hop | nothing | hop → kick-off (two rises); SPACE-SPACE-Q dies | `RideAirJumpMul`, one check | a bike that hops but does not double-jump mid-air — the middle reading: the air double jump is the most "carried-over running system" of the three |
| **(d) One shared air budget** (BIKE-1b's own question 1, still unanswered) | bunny-hop | first air action only | any *one* of {air jump, air mount, kick-off} per airtime | no verb dies; the stack is priced instead | keeps every 2026-09-01 ask, answers ruling 2's "too much inheritance" as "too much stacking" |

Option (d) note: it also caps the four-rise foot chain (SPACE, SPACE, Q-mount, Q-kick-off)
the BIKE-1b report flagged as "probably too much" — one flag, per that report.

---

## 5. FORK — the mechanical shape (Talon's)

**Trigger:** ripe now for the *lab* step (A is reversible and cheap); the A→B step ripens
when Talon rules the bike into the game (the parked BIKE-3 gate — "keep, change, or drop"),
and per the no-decisions-without-substrate principle the B commitment should not be made
before that ruling exists. **Options:** §2's A / B / C. **Tradeoffs:** §2's cost lines and
§3's table. **Recommendation on record:** §3. The C trigger (BIKE-3C finding bike-native
simulation is required) is named in §2C.

---

## 6. Bible check

```
Bibles applied:  Mechanics (a state machine's boundaries, races and idempotency are this
                 document's whole subject); Design (ripeness of §4/§5, commitment-ladder
                 stamps). Behavior/Interaction/Level do not bind a design audit that adds
                 no entity, input or composition; Thrill is not invoked — no affect is
                 directed here, and the one feel-adjacent call (how the mount should READ)
                 is routed as a fork in the companion doc, per /direct's charter.
Items checked:   MECHANICS §1 (boundaries: the 0.20 s crouch window and 0.18 s landing
                 window are cited with their inclusive semantics, not paraphrased);
                 §2 (state-transition integrity: audit rows 13–14 are exactly the
                 "dependents not updated together" defect, named as such); §3 (races: §4's
                 options each state what the T4 jump-precedence rule implies for SPACE);
                 §4 (idempotency: option B's budget bits replicate so a replay cannot
                 re-grant a spent burst — the AirJumpsUsed argument, cited).
Result:          pass — the audit found two unhandled state-cascade defects (rows 13, 14)
                 and recorded them as mask requirements rather than leaving them implied.
```
