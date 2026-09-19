# Talon's playtest notes — 2026-08-28 movement session

**Status: NOTES ONLY. Nothing here has been acted on.** This file is a capture buffer, kept
during a live feel session on `feat/2026-08-27-move-5-verbs`. Talon's standing instruction for
this session was *"please do not act on these now — I will give you lots and we can fix them all
later"*, so every entry below is recorded verbatim in intent and left open on purpose.

Add to it during play. Triage it afterwards, once, rather than fixing entries one at a time as
they arrive — that is the whole reason it exists as a file instead of as a chat message.

---

## Open notes

### N1 — the camera-facing hover UI has to go
> *"I hate the UI choices in the game the ones that are always facing the camera and that hover
> over things. These need to be taken out for sure."*

Billboarded world-space UI — the labels and markers that rotate to face the camera and float above
the thing they belong to. Talon's verdict is removal, not redesign.

- **Scope is not yet established.** The playground itself draws none of these; they are the game's,
  so this note is against the shipped world and possibly against the bubble-test scenes, not
  against MOVE-5. Establish the inventory before anything is deleted — nameplates, prompts,
  markers and section labels are probably four different systems with four different owners.
- Related: the bubble-test wave has its own note about section labels rendering through the world
  (`fix(bubble-test): section labels stop rendering through the world`), which suggests at least
  one of these is already being touched elsewhere. **Check for collision before scheduling.**

### N2 — the limb-to-head green gradient is right; push it further
> *"I love the darker green of the limbs where it's dark green and then going to lighter green
> going up to the head. Can we push this a little more?"*

A **keep**, not a defect — the vertical value ramp on the greybox body reads well and Talon wants
more of it. "Push it a little more" is a direction, not a quantity; the amount is his call at the
slider, so this wants a live control before it wants a new constant.

- This is on the classic greybox (`greybox_classic`, `MODEL-1`), which is what the playground and
  the game now both use since `BODY-1` retired the gumdrop.
- **Interaction to check first:** the bubble-test wave re-derived its palette on a value ladder
  (`bf82f9d5 feat(bubble-test): re-derive the palette on a value ladder`). Pushing the body's
  value ramp may be the same question that wave already answered for the world, and the two should
  not drift apart.

> ### ⚠ N3, N5, N6 and N7 were MISDIRECTED — they belong to the bubble-test session
>
> Talon, 2026-08-28, mid-session: *"Regarding the note I gave you for earlier. I misspoke. I meant
> to give that feedback for the other agent. My bad."*
>
> **They are kept here verbatim rather than deleted, and not acted on.** A note taken in a live
> playtest is expensive to reproduce — the moment has passed and the reaction with it — so the
> right move is to hand it to whoever owns that scene, not to bin it. Every one of them is about
> the game world (exposure, the BT-8 counter, the sky, night-time darkness) and none is about the
> movement playground, which is consistent with the correction.
>
> **Whoever picks these up: read them as one problem in a sequence, not four.** The reasoning is
> under each entry, and the ordering constraint at N5 is the load-bearing part.

### N3 — the level is too bright and too washed out; push contrast and vibrancy
> *"The colors are, overall, too subtle. You did exactly what I asked for, but if there is any way
> to get these colors to be more vibrant this would be ideal. I would like there to be more
> contrast in general, it's like I'm being blinded by the brightness of the level itself and it's
> really annoying to look at."*

Two complaints in one sentence and they are **not the same fix**, which is the thing to get right
before touching anything:

1. **Too bright / blinding** — an EXPOSURE and environment problem. Whitepoint, tonemap, ambient
   light energy, sky energy. Fixing this alone would make everything darker without making
   anything more distinct.
2. **Too subtle / not enough contrast between elements** — a PALETTE problem: the colours are too
   close in value and saturation to separate from one another.

Doing (2) while (1) is unfixed will lose: saturation added under a blown-out exposure just clips.
**Exposure first, palette second, and re-judge in between.**

*Note the "you did exactly what I asked for" — this is a correction of an earlier brief, not a
report of a defect. Whoever authored the current palette followed instructions.*

### N4 — the ground wants a checker so movement is legible
> *"I would like you to update the ground plane, just what the different squares of level are
> sitting on, with a subtle checker squares pattern. This will help the player realize their
> movement and how fast, and they'll have something to gauge speed and time and distance on."*

**Acted on for the movement playground only** (2026-08-28) — `MovementCourse.CheckerMaterial`, a
1 m world-space checker at ±10% of each block's own albedo, on every course surface. The reasoning
is in that method's doc comment: a flat greybox is a bad speedometer, because perceived speed comes
from texture flowing past the eye, and without it a 5.4 m/s jog and an 8.6 m/s sprint look nearly
the same. That directly weakened the STRIDE preset, whose whole signal is speed climbing.

**Still open:** whether the game world wants the same treatment, and at what scale. Talon's phrase
"the different squares of level" reads like the bubble test's colour sections rather than the
playground — see the open question at the bottom of this file.

### N5 — the bubble counter HUD is too dark for a bright screen
> *"I don't like the bubble counter UI in the game HUD. This is far too dark on such a light
> screen."*

BT-8's counter. **Read this together with N3**: if the level is over-bright, a HUD that was
legible against a correctly-exposed world will read as a black hole against a blown-out one.
**Fixing N3's exposure may move this on its own**, and re-lightening the HUD first would then
leave it unreadable once the world is corrected. Sequence matters; do not fix this one first.

### N6 — the skybox is boring and needs replacing
> *"We need to do something about the skybox. It's awful and boring. I would like something
> different. Should I find something to test out?"*

**Answered: yes, and it is the fastest path.** A skybox is a taste decision and no amount of
describing one settles it — the same lesson `preview_avatars.gd` taught the terrain lab when a
written description failed to pick a body and a side-by-side render settled it in one look.

Also **entangled with N3**: the sky is usually the single largest contributor to ambient light in
a Godot scene, so "the level is blinding" and "the sky is boring" are very likely the same fix
approached from two sides. Do not choose a sky against the current exposure — correct the exposure
first, or the sky that wins will be the one that survives being over-lit rather than the one that
looks best.

### N7 — nothing is visible at night and the player needs a light *(bubble test — see the box above)*
> *"It's too dark to see anything when the sun is completely down and the lights are off. The
> player needs some way to light the area around them but I don't know what it should be. I would
> like them to be able to have something like a light source but I don't know what it should be or
> how they should carry or use it."*

**Half of this already exists and was not found.** `scripts/game/light/` is a ten-file glow-stick
system — `GlowStickManager`, `Bank`, `Registry`, `Gate`, `Profile`, `Visuals` with a pooled light
budget, and a `GlowStickController` input seam — and it landed recently enough to still be getting
polish (`34279600 feat(light): the glow stick makes a noise now`).

**But it is a DROPPED light, not a carried one**, and that distinction is exactly the gap Talon is
describing. `GlowStickController` turns one key press into one `ClientRequestDrop`; the server
resolves the drop point from its own authoritative avatar transform. So the verb that exists is
*leave a light behind you* — a breadcrumb — and the verb being asked for is *see where I am going*.
Those solve different problems and a game can want both.

**The design question that is actually open**, then, is much narrower than "what should the light
be": it is whether the carried light is (a) the same glow stick held rather than dropped, (b) a
separate held item that occupies a carry slot and therefore competes with whatever else the player
is holding, or (c) something worn that costs no slot. **(b) is the one with real design
consequence** — it makes darkness a resource decision instead of a lighting fix — and the two-slot
carry system already exists to support it.

**Do not spec this before checking the night pass**, though: if N3's exposure is wrong at noon it
is probably wrong at midnight too, and some of "I can't see anything" may be the tonemap rather
than an absent light.

---

## Session one — the preset verdicts, in Talon's words (2026-08-28)

The first pass through bank one. **Every verdict below drove a change or a deliberate decision not
to change** — that mapping is the point of recording them.

| # | Preset | Verdict | What was done |
|---|---|---|---|
| 0 | SHIPPED | *"still better by just a little bit"* than 4 | unchanged — it is the baseline |
| 1 | HEAVY | *"jump is too low to make it up the stairs"* | **fixed.** `JumpVelocity` 8.4 → 9.8, restoring the shipped 1.60 m apex under gravity 30. The preset was testing "cannot reach the geometry", not "heavy" |
| 2 | FLOATY | *"way too floaty, hard to control, not good"* | **deliberately unchanged** — it is the far wall of the gravity axis, and a wall that moves every time someone bounces off it stops telling you where the room ends |
| 3 | SNAPPY | *"acceleration far too fast on the initial, stopping too quick"* | **fixed.** Accel 28 → 16, decel 45 → 28. Still well above shipped (9 / 21), so still the responsive end — no longer the instantaneous end |
| 4 | HANG (isolated) | *"I like this one the best so far. I still think 0 is still better by just a little bit"* | unchanged. This is exactly the verdict a single-variable control exists to collect |
| 5 | STRIDE | *"interesting concept, I like the way it feels, but it goes way too fast by the end after a few jump chainings"* | **tamed.** 4 rungs × 1.60 (top 15.04 m/s) → 3 rungs × 0.80 (top 11.04 m/s) |
| 6 | FORGIVING | *"my favorite so far in every regard. The forgivingness is good."* | unchanged, and **promoted to the base of the whole SHIFT bank** |
| 7 | CROUCH GRAMMAR | *"good. The slide is a bit much but it could work."* | **shortened.** `SlideMaxSec` 2.20 → 1.40, `SlideDeceleration` 2.5 → 5.0 — the slide now ends on speed rather than on a timer |
| 8 | KICK | *"SO FUN! I love it."* | unchanged |
| 9 | DOUBLE JUMP | *"this one is also great I love it."* | unchanged |

**Conclusion, Talon's own:** *"My overall conclusion is 6, 8, 9."*

### What that conclusion implies, and what was built from it

6 is a **ground-and-forgiveness** tuning; 8 and 9 are an **airborne verb**. They are orthogonal —
they share no knob — so they are not competitors and do not have to be chosen between. That is why
bank two is a 2×2 rather than four more opinions:

|  | no chain | + chain |
|---|---|---|
| **Kick** | `SHIFT+1` | `SHIFT+3` |
| **Double jump** | `SHIFT+2` | `SHIFT+4` |

Every pair differs in exactly one thing, which is what makes a verdict from them attributable.
`MovementPresetTests.TheShiftBankIsAGridOverTheForgivingBase` asserts that property rather than
trusting it.

**The one open question bank two is designed to answer:** if neither chain version beats its
no-chain twin, the chain is not earning its rung — and that is a real finding about a mechanic
that currently ships at its exact no-op.

---

## Session two — the speed ladder and the skip (2026-08-28)

### RULING: the shipped ground speed is too fast. 3.8 m/s wins.
> *"SHIFT+5 feels the best. This is what I like."*

The ladder ran 3.8 / 4.4 / 5.4 / 6.4 with every other row pinned, and 3.8 won against a control
that was the shipped value. **5.1 body-lengths/sec on a 1.20 m body**, down from 7.2.

**Everything downstream was rebased onto 3.8 immediately** — the CTRL bank had been authored at 4.4
before the ladder was run, and leaving it there would have made every grammar verdict from here on
confounded by a speed already rejected. *(The rebase tripped the slide's anti-chatter invariant —
`SlideExitSpeedMps` 2.50 is not below `0.60 × 3.8` — and the preset suite caught it. Exit dropped
to 1.80.)*

### The skip was wrong, and the way it was wrong is the lesson
> *"Players like to jump while they're roaming around because it's fun… they would hop because
> they're bored initially but they would keep hopping because skipping was fun and it was just a
> rhythm they could get into. The reason I don't like SHIFT+9 is because there's that initial jump
> which makes them feel like the initial jump is broken. Slowing them down and making them feel
> like the jump overall is tiny and weak."*

**The error:** the first SKIP preset dropped `JumpVelocity` 8.4 → 4.6 to make a skip cadence
reachable. That made *every* jump small — including the very first one, the idle hop a bored player
takes before they know a rhythm exists. **A mechanic whose invitation is "hop around for fun"
cannot open by making the hop feel broken.** The preset destroyed the thing it was built to enable.

**The fix needed no new machinery.** `JumpVelocity` returns to the shipped 8.4 and the cadence comes
from the player's thumb instead — the release cut already does exactly this:

| input | result |
|---|---|
| hold SPACE | the full 1.6 m jump. Nothing taken away |
| tap SPACE | a ~0.4 m hop, roughly a third of a second airborne |
| tap **in rhythm** | the chain ladder climbs and you get faster |

Six rungs at 0.80 over the 3.8 base tops out at **8.6 m/s** — precisely the sprint speed the slower
base gave up, handed back to a player who earns it.

**The general lesson, worth keeping:** a rhythm mechanic must not change the un-rhythmic case. The
skip cadence was reachable at the shipped tuning the whole time; what was missing was never the
physics, only the reason to tap and the feedback for tapping well.

### The single jump has to feel good on its own — and something is stealing its speed
> *"I see the speed climb. I see the rhythm the player needs to 'tap' at to make this happen. It's
> hard to do, which is okay, but there's no reason 'why' it's working or why it's not working. I
> think it's that initial jump. It's something that's making it feel like there's an initial pause
> where the player loses speed and this is not what I want them to feel. I want a jump to feel good
> if they only do one jump."*

**Two mechanisms found in the motor, both real, both making hop #1 the odd one out.**

**1. The airborne ceiling freezes the acceleration ramp.** `AvatarMotor.AirborneWishSpeed` enforces
spec §2.3, *"nobody gains speed in the air"*:

```
desired = min(requested, max(flatSpeed, groundWish))
```

While airborne the wish is capped by the speed already carried. So **a jump taken before the ground
ramp finishes freezes you part-way up it** — you cannot keep accelerating until you land. At
`Acceleration = 9` with a 6.08 m/s sprint target that ramp is about **0.68 s**, so nearly every
casual roaming jump lands inside it.

**2. `MomentumGranted` is false at chain depth 0.** `(chainDepth > 0 && ChainBonusMps > 0) ||
airJumpsUsed > 0`. Hop one is therefore the *only* hop where the ceiling can actively brake speed
off rather than merely refuse to add it; hop two onward holds what it has. The first jump is
structurally the worst one in the sequence.

**Second complaint, separate and not yet addressed:** *"there's no reason why it's working or why
it's not working."* The SKIP line shows the lead leg alternating, but nothing tells the player what
they DID to earn a rung or lose one. Feedback that reports the outcome is not the same as feedback
that teaches the input — and the rhythm is only learnable from the second kind.

**Test standing:** `SHIFT+0` is `SHIFT+9` with `Acceleration` 9 → 24 and **nothing else** moved,
shortening the ramp to ~0.25 s. If the pause goes, the cause is the ramp and the fix is a knob. If
it stays, the cause is the airborne ceiling itself and the fix is motor work. Either result is
decisive, which is the point of spending a whole preset on one row.

### RESULT: the ramp is NOT the cause. The skip is parked.
> *"I don't really like either of them very much. I think it could work but might take more tuning
> of something completely different. Let's move on for now."*

`SHIFT+0` (Acceleration 24) vs `SHIFT+9` (Acceleration 9), one row apart: **no meaningful
difference.** That kills the acceleration-ramp hypothesis cleanly and leaves the airborne ceiling
(`AvatarMotor.AirborneWishSpeed`, spec §2.3 "nobody gains speed in the air") as the remaining
suspect for the lost-speed feeling — but Talon's read is that the whole approach needs rethinking
rather than another knob, and **that judgement is the finding**.

**What is worth keeping when this is picked back up:**

- The chain/skip is the only mechanic in this motor with a real skill gap, and Talon repeatedly
  responds to it in principle. The idea is not dead; this *shape* of it is.
- The two structural facts remain true and will bite any future version: the airborne ceiling
  refuses in-air acceleration, and `MomentumGranted` is false at chain depth 0, so **hop one is
  structurally the worst hop in any sequence**. A rhythm mechanic whose first beat is its weakest
  is fighting its own opening.
- The unaddressed half is feedback that teaches the INPUT rather than reporting the OUTCOME.
  Nothing built this session tells the player what they did right.

**Status: parked, not rejected.** Do not re-tune it; re-design it.

### RESULT: the wind-up grammar is rejected — and the reason is structural
> *"I hate all of them. CTRL+1, 2, and 3. The feedback is gone. The crouching should be instant —
> if that's going to work, the moment the jump button is pressed the character should be crouched
> on the ground and then when they let go they jump. The 'wind up' and release. There is no wind
> up. There is a bad release."*

**The flaw is one knob doing two jobs.** `JumpHoldWindowSec` decides BOTH:

1. when the crouch becomes **visible** (the verb machine leaves `Normal` and enters `Slide`/`Tuck`), and
2. when the **jump is lost** (past the window you are committed to the ground).

They are the same number, so **the crouch can only ever appear at the exact instant the jump is
already gone.** The feedback fires on failure and never during the wind-up. That is not a tuning
miss — the wind-up Talon is asking for is *unrepresentable* with the current knob, at any value.

**`AnticipationMode` does not fix it either, and this was checked rather than assumed.** MOVE-5f's
coil is gated `!onFloor && localVelocity.Y > 0f` (`AvatarVisual.cs:3644`) — it is an AIRBORNE tuck
during the rise, not a grounded pre-jump crouch. `CTRL+4` would have failed the same way and one
grep saved a playtest round.

**What the fix requires:** decouple the two into separate values — a crouch that engages on the
press edge (effectively instantly), and a *separate, longer* window during which a release still
buys a jump. That is `AvatarMotor` work — shipped, netcode-deterministic code — and it is the
first thing this session has found that genuinely cannot be prototyped with knobs or an intent
decorator.

**Status: rejected as built, mechanism understood.** The idea is not disproven; the implementation
could not express it.

---

## Open questions for Talon

- **Which scene are N3, N5 and N6 about?** N5 names the bubble counter, so at least that one is the
  bubble test — but the movement playground was the scene open at the time. If N3's brightness
  complaint is about the bubble test, the work lands in a different worktree than this file sits in.

- **N1 may include the movement playground's own markers.** `MovementCourse.Marker` builds
  `Label3D` with `Billboard = Enabled` — camera-facing hovering text on the geometry, which is
  exactly the pattern N1 rejects. They are load-bearing there (they label gap widths and ledge
  heights) so they were left alone, but if N1 covers them too they need a non-billboarded
  replacement — decals on the floor would suit the checker.

---

## How to add to this file

One heading per note, `N<n>`, Talon's own words quoted, then what is *known* underneath and what
is *not*. Do not resolve a note in place — when one is acted on, say where, and leave the note.
