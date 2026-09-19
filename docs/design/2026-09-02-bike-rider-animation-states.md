# The rider's animation states — BIKE-3B

**Date:** 2026-09-02
**Packet:** BIKE-3B (systems-design)
**Branch:** `docs/2026-09-02-bike-3b-mounted-design` (base `8bf0c2a9`)
**Companion:** `docs/design/2026-09-02-bike-mounted-moveset-options.md` (the mechanics and
the forks this doc's states hang off). The bike *mesh's* motion is `BikeGreybox`'s and its
orientation bugs are BIKE-3A's; this document is the **rider's body**.
**Status:** the state machine (§2–§8) is `provisional` — buildable as written, every value
marked provisional is a knob; §9's forks are `exploring` and Talon's.

**The constraint that shapes everything here:** there are no ride clips and none can be
authored — the body is procedural. `AvatarVisual` derives gait, tilt and pose from velocity
and replicated verbs (`scripts/game/sandbox/AvatarVisual.cs`), and the body itself lives in
the .glb. So every state below is expressed as **pose targets and eased weights over the
existing rig**, in the exact contract the file's other channels already use
(`SetSwing` / `SetSkidding` / `SetVerb` / `SetCarry`: presentation-only, eased, decides
nothing — `AvatarVisual.cs:3882`, `:3908`, `:3935`).

---

## 0. Why the rider looks wrong today, in one sentence

Nothing tells `AvatarVisual` a bike exists: it derives its gear from flat speed
(`LocomotionProfile.GearFor`, `AvatarVisual.cs:4044`), so a mounted body at 9.4 m/s is in
Sprint gear running the full cadence through the frame — the field note's "running with the
bike clipped to their front" is the gait system doing exactly what it ships to do, with no
ride channel to displace it.

## 1. The one new seam, and the driving inputs

**One new presentation channel on `AvatarVisual`** — call it the **ride channel**:

```
SetRide(float weight, RideMode mode, float crankPhase01, float leanDeg, float stumble01)
```

- Same contract as `SetSwing`: presentation only, never read back by anything that decides,
  optional parameters defaulting to the exact no-op so every existing caller compiles and
  gets the body it had.
- `weight` **is `BikeLayer.Blend`** — not a second eased copy of it. One source: the tuning
  blend and the body blend cannot disagree, and a reversed blend reverses the body for free
  (§7).
- This is a shipped-file change, the same class JUMP-1 and SKID-1 made. The lab has so far
  touched no shipped file (BIKE-0's isolation rule); the ride channel is the first thing
  that cannot be built without one, because limb transforms have a single writer and it is
  `AvatarVisual`. An external pose writer (a `BikeGreybox`-style sibling reaching into the
  skeleton) would be a second writer of the pose and is rejected outright. Flagged to the
  orchestrator as an implementation-packet prerequisite.

**Driving inputs, all existing** (file: `scripts/dev/playground/BikeLayer.cs` unless noted):

| Input | Where | Drives |
|---|---|---|
| `Mounted`, `Blend` | `:159`, `:171` | which side of the machine; the ride weight |
| `SinceToggleSec` | `:174` | mount/dismount choreography clocks |
| `SinceTouchdownSec` | `:177` | landing squash/absorb timing |
| `StumbleRemainingSec`, `Stumbling` | `:161-162` | the stumble pose envelope |
| `SwingFraction`, `Swinging` | `:147-149` | the swing wind-up/release |
| `Drifting` | `:165` | the drift pose |
| `DismountArmed`, `LandingWindowOpen`, `AirMountAvailable` | `:163-168` | nothing visible (deliberate — §8, no readouts on the body beyond the pose itself) |
| `SlopeBonusMps`, `DownhillSin` | `:341-344` | torso pitch on grades |
| avatar `Velocity`, `IsOnFloor`, `VerbNow`, `Visual.BodyYaw` | `SandboxAvatar` | cadence, air poses, facing |
| handling lean (deg) | BIKE-2x harness (`BikeHandling.LeanStep` output) | torso/bike roll while riding |

**Marked NEW state** (the only additions, both named in the companion doc):

- **`ToggleKind`** — *which* of the five mount/dismount kinds last fired (ground-mount /
  air-mount / kick-off / landing-dismount / rolling-dismount). Today it exists only as the
  `LastEvent` readout string; the choreography needs the typed fact. Layer-side, one enum,
  presentation-read. Under moveset option B it is derivable on every peer from replicated
  edges instead.
- **Replicated `Mounted` + blend ticks** — required for *other players* to see a rider at
  all; this is exactly moveset option B's `MoveState` growth. Until it exists, everything
  here renders correctly for the local player only. (§8 has the full split.)

**Presentation-local accumulator:** `crankPhase01` advances client-side from wheel speed the
way `_gaitPhase` already advances from ground speed — local, derived, never on the wire.

---

## 2. The state machine

```
                 ┌──────────────────────── on-foot system (unchanged) ───────────────────────┐
                 │   Run/Jog/Walk/Idle gears · verbs · skid · jump poses · swing · carry     │
                 └──────┬───────────────▲───────────────▲───────────────▲────────────────────┘
              Q grounded│        weight │ 0      weight │ 0      weight │ 0
                        ▼               │               │               │
                 [T1 GROUND MOUNT]  [T4 LANDING     [T5 ROLLING     [T3 KICK-OFF]
                        │            DISMOUNT]       DISMOUNT        (airborne)
        Q airborne      ▼               ▲            + STUMBLE]         ▲
       ┌───────────►[S1 RIDE ·      Q in window /       ▲            Q high in air
       │             grounded]      armed touchdown     │               │
 [T2 AIR MOUNT]◄─── pedal ⇄ coast       │          Q rolling            │
       ▲                │ ▲             │               │               │
       │        leaves  ▼ │ touchdown   └──── [S2 RIDE · airborne] ─────┘
 [T6 SWING →            floor           (squash on touchdown, LandSquash)
  SLINGSHOT]        [S2 RIDE·air]
 (on-foot LMB,          │
  Q mid-swing)      RMB held, grounded ⇄ [S3 DRIFT]
```

Transitions T1–T6 are *choreographies* (clocked on `SinceToggleSec` / `SwingFraction`);
S1–S3 are *held states* (functions of live speed/lean/slope). The ride channel's weight is
`Blend` throughout; entering any T re-zeroes `SinceToggleSec` (the layer already does:
`Mount()`/`Dismount()` at `BikeLayer.cs:533`, `:545`).

**Priority ladder** (who owns the body when states overlap — one order, stated once):
incapacity park (the `AnimationSuspended` early return, `AvatarVisual.cs:3992` — dominates
everything, and the ride channel must park there exactly as the swing and crouch do) →
water → stumble pose → ride channel at `weight = Blend` → verbs/gait/jump poses (which the
ride weight displaces in proportion). Arms only: carry outranks ride (§6.3).

---

## 3. The held states

### S1 — riding, grounded: pedal ⇄ coast

**The cadence rule (proposed, provisional):** crank rate is derived from wheel speed the way
gait cadence is derived from ground speed (`LocomotionProfile.CadenceAt`,
`AvatarVisual.cs:4330`):

```
crankHz = flatSpeed / CrankMetersPerRev        CrankMetersPerRev ≈ 3.4 m   (provisional)
```

(9.42 m/s ride cap → 2.8 rev/s, the energy of a sprint; 3.8 m/s → 1.1 rev/s, a lazy spin.)

**Pedal vs coast:** PEDAL while the player is driving — forward input held and the body at
or below its current wish; COAST when momentum is doing the work — no forward input, **or**
speed above the wish (the slope carrying: `SlopeBonusMps > 0.5` or decelerating with the
stick idle). **Hysteresis 0.3 s each way** (provisional) so a rolling-resistance flicker
never machine-guns the legs — the gear-flicker defect, MECHANICS-BIBLE §2, same cure as
`LocomotionProfile`'s bands.

| | PEDAL | COAST |
|---|---|---|
| **Legs** | feet on pedals, knees driving the crank circle at `crankHz`, phase offset π between legs (`KneeBendRad`/`HipPitchRad` targets from crank phase) | cranks level (3-and-9), knees soft, weight even — the bike's idle, and the "coast / no-hands says I'm not in a hurry" candidate from the BIKE-1b expressive list, item 1 |
| **Torso** | pitched forward ~12° (provisional) + slope pitch from `DownhillSin` + roll from handling lean | more upright (~6°), same lean/slope terms |
| **Arms** | hands on bars; bar yaw follows steer (heading delta), elbows soft | same; at COAST + zero steer for >1.5 s the no-hands flourish is *available* — gated on §9 F-A2's affect ruling, not built by default |

Gait, verb and skid poses are displaced by the ride weight — at `Blend = 1` the run cycle
contributes nothing (this alone retires the field note's "running with the bike clipped to
their front").

### S2 — riding, airborne

Entered by leaving the floor mounted (ramp launch, bunny-hop, drop) — no choreography, it
is S1 with `IsOnFloor()` false. Cranks freeze level; the body stands on the pedals; the
existing airborne partition (rise-tuck / fall-reach, `AvatarVisual.cs:4171-4182`) applies at
**reduced amplitude** (~40 %, provisional) because the feet stay on the pedals — knees-up
tuck through the rise, legs pressing the bike down toward the floor as the fall builds.
Touchdown: the bike squashes (`LandSquash`, greybox-owned) and the rider's landing absorb
(`LandAbsorbSec` envelope) plays at the same reduced amplitude.

### S3 — the drift

`Drifting` true (mounted + RMB + grounded, `BikeLayer.cs:165`). Lean hard into the turn
(handling lean, sign from turn direction), **inside foot off the pedal, extended toward the
ground** — the flat-track read, and the one silhouette that says "drift" from any angle —
outside leg weighted on its pedal, torso counter-rotated a few degrees toward travel so the
head keeps leading the line. Exit eases back to S1 over ~0.15 s (provisional). No pose ever
gates the drift's mechanics (no-lockout law, `BikeHandling.cs` class doc).

---

## 4. The mount choreographies

### T1 — on-foot → ground mount (the hop on)

Trigger: Q grounded (`OnMountPress`, `BikeLayer.cs:394`). Three clocks already run:
`Blend` 0→1 over `MountBlendSec` (0.25 s), the bike's unfold over `UnfoldSec` (0.32 s), and
the hop (`MountHopMps` 2.4 → ~0.33 s of air). Rider timeline, as fractions of
`SinceToggleSec / UnfoldSec`:

| Phase | Body |
|---|---|
| 0 – 0.2 | a one-tick dip (reuse the coil shape at low amplitude) as the hop launches; gait weight starts draining (= `1 − Blend`) |
| 0.2 – 0.6 | airborne over the unfolding bike; legs split into the straddle — **left foot plants, right leg swings over** (matches the `_leadLegLeft` convention: the lead leg is the free one) |
| 0.6 – 1.0 | feet find the pedals as the spring settles (`UnfoldOvershoot` bounce is the bike's; the rider absorbs it with soft knees), hands reach the bars, torso settles into S1 pitch |

The hop's landing on the saddle needs **no landing absorb** — the bike's `LandSquash` is
the absorb, and doubling it on the body reads as two impacts from one hop.

### T2 — on-foot airborne → air mount (the burst, landing into the ride)

Trigger: Q airborne with the air budget available (`_airMountSpent` false). The burst fires
one tick later (`PostStep`, `BikeLayer.cs:668`); the bike sweeps back → under. Rider: a
compact tuck at the press (the burst's anticipation), then legs extend to *meet* the pedals
coming up under them — the body reaches for the bike, the bike rises to the feet, contact at
~0.6 of `UnfoldSec`. Hands find the bars before touchdown when airtime allows; when it does
not (a low burst), the straddle completes and the hands are still arriving during the first
grounded ticks — acceptable, never worth delaying the mount for. Touchdown lands straight
into S1/S2 rules with the bike's squash.

### T6 — the swing, and the slingshot (bike leaves the back, circles, mounts mid-air)

The swing (on foot, LMB): `SwingFraction` sweeps 0→1 over `SwingSec` (0.42 s) and
`BikeRig.SwingOffset` (`BikeRig.cs:212-218`) already places the bike — out past the
**right** hip, across the front at full reach, past the left hip. The rider follows it:
torso winds with the offset's azimuth (twist target = a fraction of the sweep angle,
~25 %, provisional), right arm extended holding the frame through the front half, weight
shifting right → forward → left. Airborne, the lunge adds a takeoff-kick-style envelope on
top. (The swing pose can also lean on the existing `SetSwing` drive rather than a new path —
implementation's call; the target shapes are what this section fixes.)

**The slingshot** (Q mid-swing, airborne): the mount fires from wherever the swing has the
bike. **Continuity rule, and it is the whole trick:** the bike travels the *shortest arc
from its current swing offset to under the body* — never via the back anchor — and the
swinging hand keeps contact with the frame until the straddle takes over (~0.5 of the mount
choreography). The read is exactly Talon's sentence: the swing's momentum *becomes* the
mount. From there it is T2's timeline compressed into whatever airtime remains.

---

## 5. The dismount choreographies

**Shared rule — "clean, immediate switching" is a per-limb handover, not a global fade.**
On any dismount, the **legs leave the pedals on the burst/hop tick** (their targets snap to
the on-foot system's, which eases them at its own rates) while the residual ride weight
(`Blend` draining over `DismountBlendSec`, 0.20 s) applies to **torso and arms only**. A
whole-body fade of 0.20 s would smear the one beat Talon prioritises; a torso that takes
0.20 s to un-hunch while the legs already run is the natural read of stepping off in motion.

### T4 — landing dismount (touch down and step off into a run)

Trigger: Q inside `LandDismountWindowSec` (0.18 s) either side of touchdown, including the
predicted armed press (`BikeLayer.cs:420-447`). The burst SETS vY to `LandDismountUpMps`
(7.0), so the body leaves the ground again immediately — and the on-foot system needs no
new pose for it: the grounded→airborne edge with real upward speed fires the existing
take-off kick by itself (`AvatarVisual.cs:4116-4137`). The design's whole job here is the
handover rule above plus one ordering fact: the bike folds (`FoldSec` 0.22 s) *behind* the
body's forward burst, so the silhouette is body-springs-forward, bike-vanishes-under —
never bike-and-body fading together. The second touchdown plays the ordinary landing
absorb, and the run is simply the gait at whatever gear the flat speed says (which is
Sprint — the point).

### T3 — kick-off (jump off mid-air, bike folds away under the body)

Trigger: Q airborne above the window (`_pendingKickOff`). Rider: push off the frame — the
takeoff-kick envelope (`TakeoffKickSec`) fired manually at the kick tick, legs extending
*downward* against the bike as vY is set to `KickOffUpMps` (6.5) — then the standard
on-foot airborne partition (tuck through the rise, reach as the fall builds). The bike
folds over `FoldSec` and returns to the back — **whether it *stays* on the back after
landing is §9 F-A1, the one unanswered BIKE-1b question this choreography branches on.**
Landing on foot over the cap meets the stumble (T5's pose) via the existing
`PostStep` touchdown clamp (`BikeLayer.cs:610-623`).

### T5 — rolling dismount + stumble

Trigger: Q grounded, rolling, outside the landing window. Two outcomes
(`Dismount(stumble:)`, `BikeLayer.cs:538-562`):

- **Under the cap / stumble off:** the hop off (`DismountHopMps` 1.6) — T1 roughly
  reversed, right leg swings back over during the hop, per-limb handover as above.
- **Over the cap, stumble on:** the clamp fires and `StumbleRemainingSec` runs (0.30 s).
  **The stumble pose** (this fills BIKE-0 open question 4, which shipped poseless):
  driven by `stumble01 = StumbleRemainingSec / StumbleSec` — torso pitched well forward
  (~25°, provisional), arms out and low for balance, gait amplitude scaled toward
  `StumbleSteerFraction` (0.35) so the steps shorten exactly as much as the steering did —
  the body *reports the mechanic* (reduced steering, no sprint, jump kept), it never adds
  to it. Legible in under a second without HUD or sound, per MECHANICS-BIBLE §2.5; and it
  removes §1.1-route-3's "stumble misread as slide" from the companion doc by making the
  stumble look like a stumble.

---

## 6. The states the prompt did not list

### 6.1 Water — mounted into the lake

Force-dismount on water entry (companion doc, audit row 13): the fold plays at `FoldSec`
while the water system takes the body; no burst, no hop. Until the mechanics land, the
pose rule alone is: ride weight to zero at water entry, water pose wins (ladder, §2).

### 6.2 Incapacity while mounted

The `AnimationSuspended` early return already dominates every channel; the ride channel
must **park** there exactly as the swing, crouch and coil do (`AvatarVisual.cs:3994-4022`) —
weight zeroed, crank phase held — or a knocked-out rider holds a pedaling pose for as long
as the body is down. The layer-side force-dismount is companion-doc row 14.

### 6.3 Carrying while mounted (shipped game only; the lab has no props)

The arms cannot hold both a crate and the bars. **Default, stated so it is a decision:**
carry wins the arms (the `CarryWeight` channel outranks ride arms in the ladder); the ride
weight still owns legs and torso, so a carrying rider pedals with the load in their arms —
degenerate but safe, never wrong-looking, never a lockout. The real design (the basket,
BIKE-1b expressive list item 8) is a BIKE-3 question and is not smuggled in here.

### 6.4 Aiming while mounted (shipped game only)

Mounting lowers the aim: aim weight zeroed while `Blend > 0`. A value call, made and
stated (ambiguity-about-a-value rule); revisit only if a mounted verb ever *wants* the aim
rig.

---

## 7. Mid-transition reversals — both directions

**The core rule that makes both cases free: the ride channel is a *scrub*, not a
timeline.** Every limb target in §3–§5 is a pure function of (`Blend`, mode, phase clocks) —
none of it is a fire-and-forget animation. The layer already allows re-mount mid-blend and
keeps the foot capture (`Mount()`, `BikeLayer.cs:524-529`), so:

- **Q during a dismount blend (re-mount):** `Blend` turns back toward 1 from wherever it
  is; the limbs retrace — legs re-straddle from their current pose, torso re-pitches. The
  bike obeys the T6 continuity rule (shortest arc from wherever the fold has it back to
  under the body — never a teleport to the back anchor first). If grounded, the layer fires
  a fresh hop (`MountHopMps`), and the body shows the dip again — honest, because the
  mechanics really did hop.
- **Dismount during a mount blend:** `Blend` turns toward 0 from a partial straddle; the
  per-limb handover rule (§5) applies from wherever the legs are. If it was a rolling
  dismount over the cap, the stumble pose takes the ladder immediately — half-straddle
  straight into stumble is correct and readable ("I bailed the mount at speed").
- **One-shot envelopes never reverse.** A takeoff kick, landing absorb or squash that has
  started finishes on its own clock while the weights reverse around it — reversing an
  impact envelope reads as the film running backwards.

---

## 8. Presentation vs simulation

| Fact | Class | Why |
|---|---|---|
| All limb poses, crank phase, lean, choreography clocks' *reads*, squash, stumble pose | **Presentation** — safe client-side | Derived from replicated state + velocity, exactly as gait/skid/verb poses are; decides nothing (the `SetSkidding` contract) |
| `Mounted`, the blend clock | **Simulation** — must replicate | They change the tuning the motor runs (where the body ends up) and every other player must see the rider; this is moveset option B's `MoveState` growth (marked NEW). Proxies adopt them per the `AdoptForProxy` default (`MoveState.cs:269`), with the known ≤0.1 s discrete-fact lead every replicated pose flag already carries |
| Air budgets, `DismountArmedTicks`, stumble clock | **Simulation** | Each changes movement (a replay must not re-grant a spent burst — the `AirJumpsUsed` idempotency argument, `MoveState.cs:216-226`) |
| `ToggleKind` | Presentation, derivable | Under option B every peer can derive which transition fired from the replicated edges; until then it is a lab-local layer read (marked NEW, layer-side) |

Deliberate non-state: `DismountArmed` / `LandingWindowOpen` get **no body tell** — the
window is input forgiveness, and a pose that telegraphed it would become a timing UI on the
body (the chain-readout prohibition's logic, MOVE-5 spec §6.6).

---

## 9. FORKS — Talon's, surfaced ripe

**F-A1 — does the kick-off keep the bike, or does the bike come back?** *(BIKE-1b question
2, asked 2026-09-01, still unanswered — re-surfaced because T3's choreography branches on
it.)* Trigger: T3 cannot be finalised without it. Options: (a) the bike folds to the back
and you land on foot at speed (as built — the fold-under silhouette, the stumble prices the
landing); (b) the kick-off is a trick, not a dismount: the bike stays under you and the
landing is mounted (T3's push-off becomes a stand-up-and-slam; the landing dismount remains
the only air exit). Tradeoffs: (a) keeps the kick-off/landing-dismount pair as two
different *decisions* (bail vs flow-through), which is the chain's depth; (b) reads better
on big drops (no bike vanishing mid-air) but collapses the two Q verbs into near-synonyms
and re-opens §4-fork option shapes in the companion doc.

**F-A2 — the mount's affect register.** The mechanics already bracket it (SNAP vs UNFOLD
presets, BIKE-1b); what the *body* sells — a matter-of-fact hop-on, or a flourish — is an
affect call, and affect goes through `/direct`, never decided inline (role charter; canon
§1.3's per-beat rule). Trigger: T1/T2's amplitude numbers (dip depth, straddle exaggeration,
the no-hands flourish gate in S1) are all downstream of it. Options: deadpan-quick /
juiced-flourish / per-preset (the body reads the blend speed and scales itself, so SNAP is
automatically curt and UNFOLD automatically showy). Tradeoff: the third option costs
nothing extra and keeps the call in the tuning row where Talon already turns it — but
whether the bike *moment* should ever be showy at all is his, via `/direct`.

**F-A3 — pedaling identity: freewheel or fixed-gear.** Trigger: S1's COAST pose branches
(level cranks vs cranks that always turn with the wheel). Options: (a) freewheel — coasting
reads as rest, the pedal⇄coast contrast carries the "am I driving?" information; (b)
fixed-gear — the cranks never stop, the Wobble identity, and the coast state visually
disappears (only cadence changes). Tradeoffs: (a) gives the expressive coast/no-hands beat
a home and is the common arcade read; (b) is more bike-honest at every speed and may be
what BIKE-3C finds worth keeping from Wobble's feel — this fork deliberately waits for 3C's
read rather than contradicting its charter. One rule swap either way; nothing else in this
document moves.

---

### §9 resolutions — added 2026-09-02 by the orchestrator, after 3B and 3C both landed

**F-A2 — RULED by `/direct`, 2026-09-02: per-preset.** The body reads the preset's blend speed
and scales its own amplitude, so SNAP is curt because it is fast and UNFOLD is showy because it is
slow. The full direction, its four constitutive constraints and the gates it was run through are
in `docs/THRILL-BIBLE.md` §12, entry *"The bike mount's affect register"* — read it there rather
than summarising it here. The four constraints in one line each, so a builder cannot miss them:
the flourish spends **no time the mount is not already spending**; **control is never gated on the
body**; it **resolves into a pose** rather than swinging back to rest; and **no second channel**
(no sting, no VFX, no camera move) is authored for it. One forward guardrail: the amplitude may
never be driven by threat state, the day a threat exists.

The no-hands coast flourish in §S1 is **not** gated by this ruling and stays unbuilt — it is a
sustained pose rather than a transition, and the F-A2 direction claims nothing about it.

**F-A3 — ANSWERED by its own trigger, not by a guess: freewheel, side (a).** This fork's stated
trigger was *"deliberately waits for 3C's read rather than contradicting its charter."* 3C's read
landed the same day and is explicit: row 3 of `docs/design/2026-09-02-bike-wobble-reusables.md`
records that Wobble's cranks are fixed-gear *and therefore never coast*, calls the freewheel the
lab's case, and hands **3B the coast threshold to pick** — which only has meaning under a
freewheel. The trigger fired with an answer in it, so the fork is recorded as answered rather than
resolved by inference. BIKE-4A builds the pedal ⇄ coast contrast on that basis and states its
chosen threshold in its report.

**This is one rule swap if Talon disagrees**, exactly as this section already says, and nothing
else in this document moves.

**F-A1 remains OPEN and is Talon's** — it is a mechanical fork about what the kick-off verb does,
not an affect call, so `/direct` neither took it nor may take it. It sits with the §4 jump fork in
the companion doc, which is the same conversation.

---

## 10. Bible check

```
Bibles applied:  Mechanics (the state machine's transitions, priority ladder, hysteresis
                 and reversal rules); Interaction (every press's visible answer — each Q
                 outcome has a distinct body read); Design (fork ripeness, commitment
                 stamps). Behavior does not apply (nothing here acts on its own); Level
                 does not apply (no composition). Thrill: deliberately NOT applied inline —
                 the one affect call found (the mount's register) is routed to /direct as
                 fork F-A2, per the role charter.
Items checked:   MECHANICS §2 (state-transition integrity: incapacity/water park rules
                 §6.1–6.2 update the dependents together; the ladder gives every overlap
                 one owner); §2.5 (perceivability: pedal vs coast, drift, and stumble each
                 distinguishable from normal riding AND from each other in under a second,
                 sound off — the stumble pose exists precisely to pass this); §3 (races:
                 reversal rules §7 define both same-blend orderings); §1 (boundaries: the
                 coast hysteresis and the 0.18 s window's no-tell rule state their edges).
Result:          pass — with two findings folded in rather than left implied: the stumble
                 shipped poseless (BIKE-0 open question 4; §5-T5 fills it) and the ride
                 channel requires the first shipped-file seam of the bike program (§1,
                 flagged to the orchestrator).
```
