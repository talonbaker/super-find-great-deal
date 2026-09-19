---
type: session-plan
for: Talon
date: 2026-09-02
status: draft — sections 4 and 5 are filled in when BIKE-4A and BIKE-4B land
summary: One ordered ride for tonight's session. Everything from the BIKE-3 and BIKE-4 waves in a single sitting, in the order that makes each thing judgeable, with the questions asked where the ride answers them.
---

# Tonight's bike session — one ordered ride

**What this is.** You said you would test everything at once tonight. Three waves of work land
between now and then and they are not independently judgeable — the wobble reads differently
once the rider stops sprinting, and the jump fork reads differently once both are in. So this is
one route through the lab, ordered so that each thing is in a state where your reaction to it
means something.

**What it is not.** It is not a checklist you owe anybody. Every step names what to look at and
why; skip any of them. The only steps that are load-bearing for *other* work are marked
**[ANSWERS A FORK]**, and those are the ones where your reaction unblocks a packet.

**Notes go in `docs/playtest/2026-09-02-talon-notes-bike.md`**, verbatim and numbered, per
`docs/playtest/INTAKE.md`. Do not tidy them; the phrasing is the information.

---

## 0. Launch

**From the `Watis_Game` repo** — the lab and the bike came over from `Sail` on 2026-09-04
(MOVE-1), and this is where they live now. `Sail` is the historical record; do not launch it
there and do not expect a change made there to show up here.

    powershell -File tests/Run-MovementPlayground.ps1

or, straight from the engine:

    scenes/dev/MovementPlayground.tscn

Godot CLI flags go **after** `--` or they are silently swallowed and it looks exactly like a hang.

The lab opens on **Flow**. The bike park is **TAB ×4** from there (course order: Calibration,
Flow, Scramble, Surf, Descent, **Bike Park**, Rolling). That distance is itself an open question
— see §6 — and it has deliberately not been changed for you, because changing where the lab
opens is your call, not an agent's.

**The keys you need tonight**, from the lab's own on-screen readout rather than a second copy
that goes stale:

| | |
|---|---|
| `WASD` / mouse | move, look |
| `SHIFT` / `L-CTRL` | sprint / walk gear |
| `SPACE` | jump — **hold** for height |
| `Q` | bike out / bike away. In the **air** = burst mount. As you **land** = burst. **Rolling** = stumble dismount |
| `RMB` | slide, and **drift** while mounted |
| `TAB` / `R` / `ESC` | next course / respawn here / free the mouse |
| `F1` | motor knob panel (arrows select, LEFT/RIGHT nudge, SHIFT ×10, HOME reset row) |
| `F10` | **bike** knob panel — same keys, and it owns the arrows while it is up |
| `ALT+0-9` | bike presets |
| `CTRL+ALT+0-6` | handling presets (1 = no turn curve, 3 = wiggle charge, 6 = handling **off**) |
| `SHIFT+F10` / `ALT+F10` | reset all bike rows / save `user://bike-tuning.txt` |

---

## 1. First — ride it before you look at anything

Get on the bike in the park and ride two or three laps without reading any of the rest of this
document. **This is the only untainted read you get all night**, and the four field-test issues
you filed this morning were all things you noticed this way. If something is wrong, you will
know before you can say why; write the sentence down before you diagnose it.

---

## 2. The orientation fixes — the three things you filed this morning

All three are fixed and measured. What you are checking is that the measurement matches what
your eyes say, because two of these were *proven correct on film* before and were still wrong in
your hands.

**2a. The heading.** Ride a full circle. The bike should point where you are going, continuously,
all the way round. Previously it did not: `BikeLayer.FacingOf` was reading `AvatarVisual.BodyYaw`,
which is rig-local torso twist — fidget and swing coil — and not facing at all. The body itself
*was* rotating every tick; the code comment claiming the character body never turns was simply
false and has been corrected. Measured: worst-case heading error **176.57° before, 0.00° after**
over 77 samples.

**2b. The lean.** Turn left and turn right at speed, at several headings — **especially heading
north (+Z), not just the direction you happen to spawn facing.** The lean was never wrong in the
maths; it was drawn in the frozen frame, so it mirrored whenever your real heading was more than
90° from world −Z. That is why it looked correct on the earlier film: that film was shot near the
frozen frame, where the two agree. Fixing the heading fixed the lean with it — **54/54 samples
agree, zero sign flips.**

**2c. The stowed bike.** Put the bike away and look at your own back (third person, or the
capture stills at `docs/qa/2026-09-02-bike-3a/`). It should sit **flat** against you. It was
edge-on: the stow branches pitched the folded stack about X only, leaving the wheel discs' normals
pointing sideways. A 90° stow yaw on the same travel scalar puts them on ±Z. **The fold itself is
untouched** — you approved that, and the flight choreography was not altered.

**One thing here is honestly unverified and you are the judge of it:** the *mid-flight* fold
blend was checked at its endpoints, not frame by frame. If the fold looks wrong in the middle of
the arc, that is a real finding and not something the measurements would have caught.

---

## 3. The mounted moveset — what is inherited and what is masked

Read nothing here until you have ridden §1 and §2. Then: **jump on the bike, slide on the bike,
and notice how each one reads now.**

Two findings worth having in your head before you form an opinion:

- **The slide was never ruled in.** Nothing in any ruling ever put a slide on the bike; it is
  reachable through the crouch grammar's held-jump trigger, which is a different path from the
  slide button. It gets masked under every option on the table.
- **The jump was ruled in, by you, deliberately.** On 2026-09-01 you asked for a double jump on
  the bike as well as off it, plus the jump-off tolerance window. `RideJumpMul` and
  `RideAirJumpMul` are tuned multiples built to that ask, and the kick-off and landing-dismount
  chain was built on top of them the same day.

**[ANSWERS A FORK] The jump fork.** Your 2026-09-02 note said jump shouldn't have carried over.
Both statements are yours and they cannot both stand as written. The four options are in
`docs/design/2026-09-02-bike-mounted-moveset-options.md` §4 — keep the chain as built, strip SPACE
while mounted entirely, keep the hop but strip the air jump, or give the air one shared budget so
any *one* of {air jump, air mount, kick-off} fires per airtime. The `Q` chain survives all four;
it never used SPACE.

**The reading that may dissolve it, and the reason this step comes after §4:** a bunny-hop
performed by a body running the sprint gait looks exactly like a running jump. Your note may be
about the *presentation* rather than the mechanics — which is what §4 changes.

**Also relevant, and you should know it before you rule:** canon §1.5 (drafted 2026-09-01 from
your own brief, still awaiting your correction) says the bike is *"summonable on a button… mount
and dismount chainable with jumps."* That is a draft an agent wrote from your words, not a ruling
you signed — flagged so you are not surprised by it later, not offered as an argument.

---

## 4. The riding pose — BIKE-4A **(landed)**

This is your issue 3, the visual half. `AvatarVisual` did not know a bike existed — it picked its
gait from flat speed, so a mounted body at the 9.42 m/s cap sat in **Sprint gear running a full
sprint cadence**. That was the running-with-a-bike-attached you saw.

**How to see it:** mount with `Q`, then **hold W and release it**. That is the whole test — the
legs should drive the cranks while you are on the power and hold still the moment you coast.

**What to look at, in order:**

1. **The gait is gone.** A mounted body short-circuits to Idle at any ride weight, so it can never
   reach Sprint. Measured: on foot at 6.08 m/s the gait is Sprint at 3.70 steps/s; mounted at
   9.42 m/s the gait cadence is **0.00** and the cranks turn at 1.60 rev/s instead.
2. **The cranks are a speedometer.** Crank rate is the fixed-gear rule from the Wobble audit —
   **5.875 m of ground per crank revolution**. It scales exactly with speed and stops dead at rest.
   If the pedalling ever looks like it is running on its own clock rather than off the ground,
   that is the thing to say.
3. **PEDAL and COAST are two distinct poses** — 12° of forward torso pitch on the power, 6° when
   coasting, with a 0.3 s hysteresis each way so it cannot flicker. The coast threshold is
   **0.22 m/s**, which the agent picked itself: the design specified the trigger but nobody had
   ever specified the speed.
4. **Airborne keeps the pose** at 40% scale, cranks frozen — you are still a rider in the air, not
   a runner.

**[ANSWERS A FORK] — the one I had to make at integration, and it is a one-symbol change.** The
rider had to be handed the machine's roll, and there are two of them: the bike mesh is drawn at
**lean + wobble**, and I gave the rider **the corner lean only**. So you bank with the bike through
corners, and §5's low-speed wobble happens *under* you — which is what makes it read as the machine
being unsteady rather than the whole world tilting. Hand the rider the full roll instead and the
wobble goes invisible relative to the bike and only shows against the horizon. **Ride it and tell me
which reads better**; it is one symbol on one line either way.

*(I also had to stop the body banking twice. The rider's own lateral-acceleration bank was being
computed whether or not you were mounted, so adding the machine's lean on top would have leaned you
through every corner twice over. They now crossfade on ride weight. On foot, nothing changed at
all — the expression is byte-for-byte what it was.)*

*Ruled by `/direct` today and worth knowing while you look at it:* the **mount's** register is
**per-preset** — the body derives its own amplitude from the preset's blend speed, so SNAP is
curt because it is fast and UNFOLD is showy because it is slow. The constraint that matters most
is that the flourish spends **no time the mount is not already spending**: if getting on ever
feels slower than it did, that is a defect, because canon §1.7 makes *stopping should feel bad*
the thing the whole build is for.

*Evidence: `docs/qa/2026-09-02-bike-4a/` — four beats before/after from the side, A/B inside one
run. Note the older `2026-09-02-bike-2x-l` set is **not** a valid before-picture: it predates the
stow fix in §2c.*

---

## 5. The low-speed wobble — BIKE-4B **(landed)**

The lab bike was dead calm at walking pace, which the Wobble audit named as the single most
un-bikelike thing about it. A real bike is unstable at walking pace and settles as it speeds up;
that coupling is Wobble's entire feel. This adds a presentation-only oscillating roll at low speed
that fades out by the ride jog. It feeds the visual roll and **nothing else** — never the turn
multiplier, never tuning, never velocity, never the drift state.

**Where to feel it:** the **Bike Park HubPad**, coasting **under 4 m/s**. Above that it is gone by
construction, so if you only ride fast you will never see it and that is correct.

**The knobs — `F10`, HANDLING group:**

| Row | Default | What it is |
|---|---|---|
| `WobbleAmplitudeDeg` | **2.50** | peak degrees of roll at walking pace |
| `WobbleFadeSpeedMps` | **4.00** | the speed it is fully gone by |

`CTRL+ALT+6` turns handling off entirely if you want the before-picture back mid-ride.

The fade default comes from the Wobble audit's own measured stability crossover (~4.3 m/s), pulled
slightly earlier because in Wobble the wobble was caused by *pedalling* and here it is idle
texture. The amplitude was deliberately set under 3° so it cannot flip the sign on BIKE-3A's lean
check — that is a safety margin, not a feel judgement, and it is exactly the number you should
push on.

**[ANSWERS A FORK] — and it is a knob, not a yes/no.** Turn `WobbleAmplitudeDeg` up until it is
obviously too much, then back down until it stops being noticeable, and tell me the two numbers you
passed through. That bracket is worth more than an opinion about whether 2.50 is right.

*One usability fix came with it: the panel gave every degrees row a 0–90 range, so a nudge on a
2.5° default moved it 0.9° — a knob you could see and not tune. Small-default degree rows now have
a usable range; the three pre-existing angle rows are asserted unchanged.*

*Evidence if you want it before you ride: `docs/qa/2026-09-02-bike-4b/` — 24 headed frames (walking
pace vs ride speed) and a plotted roll series.*

---

## 6. The questions, in the order the ride answers them

Answer them in the notes file, in your own words, one line each. None of these is urgent enough
to interrupt a ride for.

1. **The jump fork** (§3) — which of the four, or "ride it again once the pose is in".
2. **The wobble numbers** (§5) — the two amplitudes you passed through.
3. **The 3B recommendation** — mask the mounted verbs now (option A) and revisit at ship
   (option B), which is what BIKE-3B ranked against your tenets. Yes, no, or a different pairing.
4. **The five feel questions from this morning** — the drift charge model (`CTRL+ALT+0` vs `3`),
   the exit boost's price (`CTRL+ALT+4`), whether the turn curve earns its keep (`CTRL+ALT+1` vs
   `2`), the camera numbers on the two courses, and `LeanExaggeration` at 2.2. **Your first
   session's drift telemetry is void** — the drift was literally unreachable by hand until the
   base commit this work sits on, so anything you concluded about it was concluded about
   something you could not actually do. These need this session before they can be answered.
5. **The course ordering** — the lab opens on Flow and the bike park is four TABs away. Should it
   open on the bike park while the bike is what you are working on?
6. **Whether the Thrill Bible still describes this game.** Its doctrine was decided 2026-07-26
   against the camp horror game — *"comedy-forward is dead"* — and canon §1.4 now says the game
   does not take itself seriously and that looking stupid is a feature. Both are your words, ten
   weeks apart, and the second one replaced the premise the first was written for. Filed as a ripe
   fork in `docs/THRILL-BIBLE.md` §13 with its three sides. **Not tonight's question** — it is
   here so it is not lost, and so nobody quietly answers it by inaction.

---

## 7. After the session — one thing to do before you close the editor

Copy the newest CSV out of

    C:/Users/talon/AppData/Roaming/Godot/app_userdata/Watis World/bike-telemetry/

into `docs/qa/` beside the last one, and commit it. That is the only record of what you actually
did in there, and the drift telemetry in particular is the thing that replaces the void data in
§6.4. The suite law applies to that commit like any other.
