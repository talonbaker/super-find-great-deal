# Level Bible

**Applies to:** the composition of a playable level — how zones, spawns, paths, pacing,
the return home, threats and ambient legibility are arranged. Not the entities themselves. If you
are deciding how a creature *behaves*, that is `BEHAVIOR-BIBLE.md`; how a system *resolves*,
`MECHANICS-BIBLE.md`; how the player *acts on* a thing, `INTERACTION-BIBLE.md`. This file is
about the arrangement those three produce when assembled into something you can play.

**How to use:** read it, take what applies, and ignore what does not. **Nothing here gates a
level and nothing here waits on being filled in.** Build the space, walk it, change it. The
Manifest in §1 is a list of the things a level usually turns out to need an answer for — a
prompt, not a form to clear before geometry.

Place things wherever you want. Where this file says a placement tends to cause trouble, that
is an observation about what has happened before, not a boundary.

---

## 0. What this file is, and what it is not

The other four bibles have a Part I of *checks* accumulated from shipped defects. This one
does not, and pretending otherwise would be dishonest: this game has never shipped a composed
level, so there is no defect history to distil. Every section here is a **contract** — the
field set a level declares before it is built — derived from research rather than from
scars.

Source: `docs/superpowers/research/level-architecture-RESEARCH-RESULT.md`, all seven
categories, read against `mechanics-interactables-RESEARCH-RESULT.md` in the same directory.
Where that research recommended a default it is marked **[research default — pending Talon
confirmation]**; where it left a fork it stays **[BLANK — Talon]**. Do not resolve a
`[BLANK — Talon]` by inventing a value. *2026-07-22: Talon extended decision Q3 to this
research — its clean pending-default markers are decided (stamped inline below). §2.4 is
the exception (explicit pick pending, see there); `[BLANK — Talon]` forks are unaffected.*

**A level composes contracted entities; it does not define new ones.** If building a level
requires a creature to do something no Family declares, or terrain to change by a route no
writer registers, the finding is in the *entity* contract, not here. Adding a capability by
way of a level is how the contracts get quietly bypassed.

---

## 1. The Level Manifest

The set of things a level usually turns out to need an answer for. **Fill it whenever you
know — during, after, or never.** An empty field is not a problem; it is a question that has
not come up yet, and several of them cannot honestly be answered before the space has been
walked.

| Field | Declared in | Governs |
|---|---|---|
| `zones[]` + gradient ordering | §2 | safe → transition → danger ramp |
| `macro_structure` | §2.4 | linear · hub-and-spoke · open mesh |
| `spawn_contract` (players / creatures / resources) | §3 | placement, fairness, density |
| `path_topology` | §4 | single spine · parallel · mesh |
| `route_loss_rule` | §4.3 | the fairness trilemma — reads best agreeing with §6 |
| `session_target` | §5 | the keystone tuning variable |
| `pressure_driver` | `MECHANICS-BIBLE.md` §10 | not redeclared here |
| `commitment_threshold` | §5.3 | spatial · resource · time |
| `return` | §6 | the home goal, the contested return, the ritual beat |
| `threats[]` + placement | §7 | which threats instantiate here, and where |
| `urgency_cue` | `MECHANICS-BIBLE.md` §10.4 | authored with `/spec-urgency-cue` |
| `anti_unwinnable_guarantee` | `MECHANICS-BIBLE.md` §10.5 | **required, not optional** |

The last two are deliberately *not* restated in this file. They already exist as required
fields on the Pressure Driver, and two copies of a required field drift until only one gets
enforced.

*Field rename 2026-07-29: `extraction` → `return`, `families[]` → `threats[]`, per the §6/§7
re-cut. `/spec-level`'s schema still carries the old names and must follow — flagged as skill
work, not silently absorbed here.*

---

## 2. Zone & Gradient Contract

### 2.1 A zone is a bundle, not a region

A **zone** is a contiguous area with a coherent bundle of three things: terrain properties
(`MECHANICS-BIBLE.md` §9.1), an ambient signal set (§8 here), and Family presence (§7 here).
Geometry alone does not make a zone. If two areas share all three bundles they are one zone
with a shape, not two zones.

### 2.2 The gradient is a signal ramp, not a geography

Danger is communicated by **converging redundant channels**, and the ramp is near-monotonic
from the safe anchor toward the danger. The channels, in this game's order of nativeness:

- **Terrain properties** — `traction_class` degrading (grass → rock → wet),
  `surface_noise_class` rising, `flood_level` appearing, `collapsible` appearing. This is the
  strongest channel because it is already authoritative state rather than dressing.
- **Ambient audio** — soundscape shift, threat vocalisations nearer and louder.
- **Light and colour** — warm/open reads safe; cool/dark/enclosed reads dangerous.
- **Detail density and movement** — the eye goes to what moves. Young creatures and defended
  resources carry this for free.
- **Architectural framing** — narrowing compresses, opening releases.

**[BLANK — Talon]** Which channels are *mandatory* at a zone transition versus optional
flavour. The research's suggested shape is a floor of two — e.g. a danger-ward transition
changing traction **and** ambient audio — so no transition rests on a single channel. Same
logic as `INTERACTION-BIBLE.md` §8.2 applied to geography rather than events, and it goes
wrong the same way: a player whose tether has severed their audio has nothing left to tell
them the ground turned against them.

### 2.3 Zone count

**[BLANK — Talon]** The baseline count. Decide a *default*, not a law. Both failure modes are
known and neither is subtle:

- **Too few (1–2):** the gradient collapses to a binary safe/dangerous flip. There is no
  build-up, no moment of committing, and Pressure has no steps to escalate across.
- **Too many:** each zone loses identity, traversal becomes padding, and `session_target`
  balloons.

Count is downstream of `session_target` (§5.1) — beats fit in a session, zones hold beats.
Decide the session length first or this number is a guess wearing a decimal point.

### 2.4 Macro-structure

**[decided 2026-07-22 — `hub_and_spoke`]** Macro-structure is **hub-and-spoke**: the safe
zone is a central hub with several spokes reaching danger-ward. This is no longer the
research default (linear descent) nor the divergence it looked like from the first manifest
— it is the project's decided value, and `docs/levels/jungle-island-manifest.json`'s island
reading is now the *confirmed* case, not a divergence to reconcile.

Reasoning recorded: it matches the jungle-island manifest's actual island geometry, where
the hub sits high and spokes descend to the waterline. §2.4's own stated cost of
hub-and-spoke — *"Pressure must then be per-spoke or it cannot threaten anything that
matters"* — is dissolved by the standing pressure design rather than paid: a rising
Pressure Driver *surrounding* an island threatens every spoke's low ground **simultaneously
and by construction**, so one authored pressure schedule menaces all spokes at once without
per-spoke pressure authoring. Hub-and-spoke's other cited benefits (several Families in
separate spokes, a natural regroup point) apply directly.

The research default's advantage — a single spine to threaten — is preserved in spirit:
each spoke *is* a spine an encircling pressure threatens end-to-end. Open mesh remains ruled
out for the reasons stated below (it would invalidate §4.3, §5.2 and §6 together); loosening
a spoke toward parallel paths in playtest is still a tuning move, not a structural change.

The alternatives and their costs: **hub-and-spoke** supports several Families in separate
spokes and gives a natural regroup point, but Pressure must then be per-spoke or it cannot
threaten anything that matters. **Open mesh** maximises freedom and emergent routing, but
lockout becomes nearly impossible, tension-per-cut drops to near zero, and it is the hardest
to keep legible without UI.

Loosen toward parallel paths if linear feels on-rails in playtest — that is a tuning move.
Moving to open mesh is not: it invalidates §4.3, §5.2 and §6 together.

### 2.5 Zone size is netcode-sensitive — flag, do not resolve

Zone size scales against player count (1–6, designed for four — canon fact 15) between two
failure modes: **too large** leaves
players isolated beyond voice/tether range, **too small** collapses cooperative decisions into
congestion. The ceiling is set by the tether's actual range, which is a netcode value this
file does not own. **[BLANK — Talon + netcode]** target traversable area per player.

*Scope note: written 2026-07-20 from research, against a Playground level that is a blockout
with density and verticality passes but no authored gradient and no terrain-property system to
carry one. Current bite: none — no zone in the build declares a bundle. First real test: the
level. Passing looks like a player walking danger-ward and naming two channels that told
them so, without being asked to look for them.*

---

## 3. Spawn Placement Contract

### 3.1 Spawn placement — what has surprised people

`BEHAVIOR-BIBLE.md` §1 covers the same ground for entities: valid ground, not sealed behind
unreachable geometry, a logged fallback rather than a silent (0,0,0). This section adds *where
relative to the gradient*.

One case is easy to miss: a spawn that is reachable **today** but only across a route Pressure
can destroy stops being reachable the moment that route goes — see §4.3. Worth knowing about;
not a reason to place it somewhere else if that is where you want it.

### 3.2 Player spawn

**[decided 2026-07-22 — Q3 extended]** Players arriving **together**, in the safe zone, in
sight of each other, with danger travelled *to* rather than spawned *into*. Recorded as the
project's standing preference and the reasoning is below; a level that wants something else is
a level that wants something else.

Together-and-visible is not politeness. A cooperative-decision game whose whole texture is the
conversation about pushing deeper needs the group bound before the first decision; scattering
fractures it before voice or tether can do their job. The range is 1–6 and the case this is
designed for is **four** (canon fact 15, Talon 2026-08-21) — at four, one scattered player is a
quarter of the conversation gone. Solo is supported, so a level may never *require* a second
player to be legible.

**[BLANK — Talon]** whether any level type may stagger or scatter spawns deliberately.

### 3.3 Creature and resource spawn

Three forms, declared per level and per category — they may differ:

- **Fixed** — authored, fully legible, learnable; stale on repeat.
- **Randomised-within-region** — replayable, and preserves zone identity provided the region
  respects the gradient.
- **Density-based** — density rises danger-ward. The natural fit for a gradient, and the
  natural fit for Pressure escalation.

**[BLANK — Talon]** the default per category.

### 3.4 Early risk

**[BLANK — Talon]** Is the safe zone strictly safe, or does a small amount of risk sit near
the transition — one young creature within tempting reach — to make the opening tense?

This sets the emotional temperature of the first minute and it is a taste call, not a value.
Note that it interacts with §7.3: a Family placed near the safe zone is the *first* Family
players meet, which makes it the one that teaches them what a Family is.

### 3.5 Scaling with player count

**[BLANK — Talon]** whether spawn density scales with player count, and linearly or with
diminishing returns. Note this is a Pressure interaction as much as a spawn one — more players
means more noise into the one shared channel (`BEHAVIOR-BIBLE.md` §9.1), so a level that also
scales density is escalating twice.

*Scope note: written 2026-07-20. Spawn code today places players at authored points with no
zone concept and no density model. Current bite: none. First real test: the first level with a
declared gradient. Passing looks like every spawn category naming its form, and no creature
spawning in a zone whose bundle it does not belong to.*

---

## 4. Path & Traversal Contract

### 4.1 Topology

- **Single spine** — one route out and back. Maximum pacing control, and Pressure can threaten
  *the* route, which is the most legible and most dramatic version of route destruction. Costs:
  a destroyed spine is a hard fairness problem, and backtracking is the same corridor twice.
- **Parallel paths** — redundancy makes a cut survivable and lets the group split. Each cut is
  less dramatic; pacing is looser.
- **Mesh** — emergent routing, near-impossible to cut, lowest tension per cut.

Declared as `path_topology`. It and `macro_structure` (§2.4) are two views of one decision, so
they tend to want the same answer.

### 4.2 Verticality is a composition primitive

A high-ground→low-ground shape makes gravity a level-design force, and it is **asymmetric on
purpose**: descending is cheap, ascending is expensive. That asymmetry buys two things for
free:

- A drop the player can slide down but not climb back up is a **spatial point of no return**
  (§5.3) that needs no trigger volume and no message — the geometry *is* the commitment.
- The return leg under rising Pressure is harder than the outbound leg, which is the correct
  shape for a tension curve, and it costs nothing to author.

Traversal verbs available to a level are climb, slide, and fall, and they compose with terrain
properties — a wet `traction_class` slope is a different object from a dry one, and a
`collapsible` ledge is a different one again. Traversal gated on a capability grant is
`MECHANICS-BIBLE.md` §9.4's two-check composition; this file does not redefine it.

**[decided 2026-07-22 — `bidirectional`]** Verticality is **bidirectional**: climbing is a
repeatable traversal verb, not a one-shot commitment. This matches the standing
jungle-canopy-climb design direction, where the canopy climb is meant to be a verb players
use again and again, not a door that locks behind them.

This rules out **hard one-way locks only — not difficulty.** The
descending-cheap/ascending-expensive asymmetry stated above this decision still fully
applies: ascent stays expensive, the return leg under rising Pressure stays harder than the
outbound leg, and time-based commitment cost (§5.3) still gives verticality real teeth. A
drop can still be a *practical* point of no return under a pressure clock (climbing back may cost
more time than you have) — what `bidirectional` forbids is geometry that makes the climb
*impossible*, not geometry that makes it *costly*. Specific one-way drop volumes remain
authorable per level as a deliberate exception, but the level's default is that verticality
can be re-traversed both ways.

### 4.3 Route loss — the fairness trilemma **[decided 2026-07-22 — `conditional_on_overcommitment`]**

**Decided: `conditional_on_overcommitment`** (the literal enum from `MECHANICS-BIBLE.md`
§10.5 — this is the *same field*, not a new one). Route loss and the contested extraction of
§6.2 are both answered by this single value: a route is lost only by **staying too long or
carrying too much**, never by an unavoidable cut a fair player could not have foreseen.

Reasoning recorded: with decision B4 (nobody holds the terrain pen before the first
playtest — `docs/superpowers/audits/2026-07-20-fable-grand-review.md` §B4), the *only* thing
that could cost a route at all was the pressure, and a map-wide pressure **is** an over-commitment
clock by nature — stay too long on low ground and it floods, exactly the greed-punishing
shape this enum names. So the decision is close to already-true by construction rather than a
new system to build. It matches `DESIGN-BIBLE.md`'s proportionate-consequence principle and
the downed-vs-dead tone: mistakes are recoverable unless you push your luck. `always_one_exit`
would blunt the pressure's teeth; `total_lockout_permitted` is disproportionate for a first
playable with no reliable Parent-kill valve. This is the project's standing answer for §10.5
where a level has no reason of its own to differ.

The trilemma's three options are retained below for reference; the decided one is the middle:

If Pressure or a Parent's `terrain_alteration` response can destroy or block a route, the
level guarantees exactly one of:

- **Always-a-route-back** — at least one return path is protected from loss. Fair, forgiving,
  lower stakes.
- **Intended total lockout** — sealing the level is a designed failure state. Highest stakes;
  requires the player failure contract (`BEHAVIOR-BIBLE.md` §10) to make it land as earned,
  and requires §8 to have warned them.
- **Conditional on over-commitment** — loss is reachable only by staying too long or carrying
  too much. The greed-punishing option, and the one that makes the carry decision
  (`MECHANICS-BIBLE.md` §8.3) matter at level scale.

**This is one decision, not two.** The same rule governs route loss here and contested
extraction in §6.2 — resolving them separately is how a level ends up promising a fallback on
the way out and revoking it at the door.

**It is also not a new decision.** `MECHANICS-BIBLE.md` §10.5 already asks every Pressure
Driver the same question per level — whether an exit always remains or lockout is deliberate.
This
section is that same field seen from the geometry side. Answer it once; `route_loss_rule` and
`anti_unwinnable_guarantee` are the same answer written in two places, and if they ever
disagree the Pressure Driver wins.

The load-bearing reason it cannot be deferred: **defeating a Parent requires an earned,
per-family `defeat_method`** (`BEHAVIOR-BIBLE.md` §9.5, amended 2026-07-21 — decision A2).
Pikmin lets you kill the Bulborb by just attacking it; this game tolls that valve — it exists
only where declared, and may be unavailable in the moment. With no *reliable* kill option
and a Parent that can rewrite terrain, nothing between the player and a sealed level exists
except this rule.

*Scope note: written 2026-07-20. No terrain-state system exists, so no route can currently be
lost; the Playground's verticality pass is static geometry. Current bite: none. First real
test: the first destructible or floodable route. Passing looks like `route_loss_rule` and
`anti_unwinnable_guarantee` reading as the same sentence, and a playtest that never produces
"we were sealed in and nobody could tell why."*

---

## 5. Pacing & Escalation Contract

### 5.1 Session length is the keystone **[deferred 2026-07-22 — Talon: needs mechanics
we don't have yet; trigger: a playable loop exists to actually time. Do not pick a
number, do not prototype candidate lengths, until then.]**

`session_target` gates everything downstream: length sets beat count, beat count sets zone
count (§2.3), zone count sets how many steps Pressure escalates across (§2.4). It is the first
number to pick and the cheapest to prototype — run two or three candidate lengths before
authoring any geometry against one.

A short session holds one arc: out, a peak at the far point, and the scramble back. A longer one
holds several, or introduces Families in sequence (§7.3). Neither is wrong; guessing is.

### 5.2 Frequency, not amplitude

Pacing is built from **beats** — a harvest, a waking, a scramble — arranged into peaks and
valleys, and the tuning lever is *spacing in time*, not per-threat difficulty. Left 4 Dead's
Director is the canonical statement of this: constant combat fatigues, long inactivity bores,
and unpredictable peaks and valleys are what make a run replayable. The Director cycles build
up → sustain peak → peak fade → **relax**, and the relax period is mandatory rather than
emergent.

Here that means the escalation knob is how often a Family is provoked and how long the
quiet stretches are — not how hard the Parent hits. There is no difficulty axis on a Parent to
turn anyway (§9.5 there), which makes frequency the only real lever the design has.

### 5.3 The commitment threshold

Three forms, not mutually exclusive, but a level declares which one **primarily** drives it:

- **Spatial** — crossing a zone boundary, reinforced by the one-way drops of §4.2.
- **Resource** — how much you are carrying. This is where `MECHANICS-BIBLE.md` §8.3's
  capability denial becomes a level-scale mechanic rather than an inventory rule: the arms
  full of young *are* the commitment.
- **Time** — Pressure passes a threshold past which return is contested.

### 5.4 Threat coexistence is gated, not capped

There is no maximum number of Families or hazards. The limit is enforced by **gating peaks**,
which is what the shipped prior art actually does: Deep Rock Galactic suppresses swarms
entirely while a Dreadnought is alive; L4D spaces threats temporally rather than thinning
them.

**[BLANK — Talon]** the gating rule. The shape the research suggests is a ceiling of one
Family at full escalation at a time — a second Family may be present, provoked, and audible,
but not simultaneously peaking. Note this rule is what makes §7 tractable: without it,
"several Families in one level" resolves to noise.

*Scope note: written 2026-07-20. No pressure system, no beats, no session target; playtests to
date have been open-ended sandbox time. Current bite: none. First real test: the first level
with a Pressure Driver. Passing looks like a run whose intensity, if plotted, has visible
valleys, and a player who can point at the moment they committed.*

---

## 6. The Return Contract

*Re-cut 2026-07-29 for the come-home-before-dark loop. This section was the Extraction Contract;
there is no extraction in a game about coming home before dark. The structural role the old
contract protected — a known goal, a return trip that is the level's second half, tension
collected at the end — survives intact and is re-grounded below on
a loop design that has since been retired. Field renamed `extraction` → `return` in §1;
`/spec-level`'s schema follows.*

*De-named 2026-08-18: this section used to be written around one specific cast and one
specific map. What it was actually saying generalises, and is kept in that form.*

### 6.1 Category

A **home location** that is spawn, refuge and goal at once. Fixed, **known from the first
second**, and every outing bends back toward it: the period ends on arriving home, and what
was gathered counts only when it gets there.

The old contract's reasoning carries forward whole: a known home makes the return trip the
day's second half rather than an epilogue, and it is what makes "push deeper or head back?"
an actual conversation rather than a gamble. What changed is the deadline's shape — the goal
can end a period rather than the whole session, so the return happens repeatedly.

**[BLANK — Talon]** known-from-start versus discovered stays recorded for any future map
that departs from the default.

### 6.2 The contested return

A contesting force worth the name is one that **converges on the home location** with a front
slightly faster than a walking player — escapable with planning, barely. Route loss under such
a thing is governed by §4.3 — **`conditional_on_overcommitment` decided there (2026-07-22)**;
it is the same field, and a converging boundary is exactly the overcommitment case that
decision priced. Not restated — see the note there.

**Where a return is priced against two clocks, their interaction is one decision, not two.**
A time-driven clock that resets and an obligation-driven one that only ratchets both bear on
the same choice — stay out or turn back — and typically the thing that satisfies the second
requires exposure to the first. Whether the second may worsen the first, whether it can be
paid ahead, and which combination is allowed to end a run belong together rather than as
three separate tuning values.

### 6.3 The ritual is the climax beat

Whatever §5.3 chose as the commitment threshold, **the arrival home is where it gets
collected**. A period whose tension peaks *before* the return has mis-composed its pacing: the
outbound leg is the quieter one, and whatever happens on arrival is the payoff the whole
period was priced against. Returning early is allowed and carries no reward.

The shape worth stealing from the retired version: the collection is something the group
*watches happen* rather than something a screen reports, and how much of it they can see is a
choice they made earlier.

*Scope note: re-cut 2026-07-29, de-named 2026-08-18. Nothing in this section is code — no
converging boundary, no arrival ritual, no collector on any merged line. Current bite: none.
First real test: the first level composed with a return. Passing looks like a group audibly
arguing about whether to go back out for one more thing, with the deadline visible.*

---

## 7. Threat Composition

*Re-cut 2026-07-29, de-named 2026-08-18. This section was Multi-Family Composition, then a
roster of four specific named threats. Both casts are gone; the composition law they carried
is not, and it is what remains here. It applies to any set of threats sharing a level,
whatever they turn out to be — a fixture that is always present, a threat rolled into one zone
per run, a creature with two states, a lingering after-effect. Their behavioural contracts are
`/spec-entity` work; `BEHAVIOR-BIBLE.md`'s Family machinery is cited only where its law
transfers.*

### 7.1 Threats stay independent — the law survives the roster

The decision `BEHAVIOR-BIBLE.md` §9.4 recorded for Families transfers whole: **agitation
belongs to the threat that owns it** and there is no global alarm. Waking one threat does not
anger another, and one threat's mood does not aggro a third. Per-level shared alert state was
rejected as "one global alarm" that deletes the quiet corner. The quiet corner is
worth protecting in its own right — it is where a plan gets made, and a plan is what the
comedy needs in order to go wrong.

The prior art stands as recorded: Rain World has no global aggro bus; Shadow of Mordor built
propagating multi-faction state and cut it in pre-production; immersive sims propagate alerts
within a faction and diegetically, never across factions.

**[BLANK — Talon]** — still the one genuinely open half — whether one threat's action may
affect another as a **physical consequence**. Noise made fleeing one threat arriving at
another's hearing through the one shared noise channel is not cross-threat aggro; it is a
sound something heard. The precise statement carries forward: **diegetic and physical, yes;
abstract shared alert flag, no.** A shape worth keeping from the retired roster: a threat whose
anger is fed by *the players'* mistakes and by time — it only ratchets, and no other
threat writes to it.

### 7.2 Separation devices

Three, combinable. **[BLANK — Talon]** which leads:

- **Spatial** — fixed topology: the compass does not change and each threat keeps its own
  region. Lethal Company's hard indoor/outdoor segregation is the functional reference; an
  interior/exterior seam is the cheapest version of it.
- **Thematic** — threats distinct enough to read as separate systems even when co-located. A
  threat whose harmless form is *cute* is this device inverted deliberately.
- **Escalating introduction** — a dangerous window that grows as players get equipped, so the
  device is structural rather than scheduled.

### 7.3 One tell, and first exposure is ordered

**[decided 2026-07-22 — Q3 extended; carries to bespoke threats]** Each threat has exactly
**one** clear sensory tell, as a cross-threat invariant — a player who has learned that tells
exist should be looking for the next one. The shape that worked in the retired roster is worth
copying: a countable feature that reads at a glance and gets more pronounced as the threat
escalates. Each threat's tell is its own entity spec's problem to declare.

**[BLANK — Talon]** whether first exposure to each threat is ordered. The isolate-then-layer
principle says yes, and a short first period answers it partly for free by metering how much a
new group can meet at once. The old rationale cited `calm` as one of two exits from an aggroed
Family; that referent is gone (see `INTERACTION-BIBLE.md` §9.2), which makes ordered first
exposure *more* important rather than less — there is no de-escalation verb at all.

### 7.4 Placement is how separation is implemented

§7.2's spatial device is `spawn_contract` (§3.3) with a per-threat region. Two placement shapes
are worth knowing: a **fixed** home and compass, and a threat **rolled at run start into one
zone, staying there all run and re-rolling next run** — learnable, avoidable, and markable by
whatever the players use to mark things. Overlapping regions mean co-located threats, and §7.2
then leans thematic.

*Scope note: re-cut 2026-07-29, de-named 2026-08-18. No multi-threat roster exists on a merged
line. The creatures that do exist are standalone entities with private aggro, which happens to
already satisfy §7.1. Current bite: none. First real test: the first night with two active threats. Passing
looks like a player naming both tells unprompted, and no code path where one threat reads
another's agitation state.*

---

## 8. Ambient Legibility

### 8.1 What this section owns, and what it does not

`INTERACTION-BIBLE.md` §8 governs whether a **consequence** is perceived and by whom.
`MECHANICS-BIBLE.md` §2.5 governs whether a **state change** is perceived by the player it
happened to. This section is the third case neither covers: whether the **level's own state**
— how bad things have got, how much time is left, where you have already been — is perceivable
without UI.

Every urgency cue is also subject to `MECHANICS-BIBLE.md` §10.4 and the redundant-channel rule
in `INTERACTION-BIBLE.md` §8.2. Audio alone is never sufficient — a player may have it masked
by proximity voice, ducked by their own settings, or absent entirely (accessibility). *(The
rule's original rationale here cited the tether severing audio; the tether was speculative and
pre-pivot, and the rule does not need it — re-grounded 2026-07-29.)* That rule is not restated
here; it is inherited.

### 8.2 The dusk sweep is the primary urgency cue

*Re-cut 2026-07-29 — this section was the advancing pressure line; it died with the
2026-07-24 kill. Its replacement inherits every property the original was chosen for.*

**The sky is the strongest ambient cue available and it is nearly free.** A visible dusk sweep
derived entirely from `CycleDriver.Phase` is self-explanatory, survives audio severance and
needs no HUD. Making the golden hour visible in both directions gives **dusk as the warning
and dawn as the relief beat**. Sun position is worth keeping constitutionally honest — always
true even where instruments lie — so the primary urgency cue cannot deceive.

Where nights lengthen across a run, the same cue carries escalation for free: the sky itself
communicates that a later night is worse than an early one. If it proves insufficient in playtest, that is
evidence about the missing failure systems (§8.5), not a reason to reach for UI first.

### 8.3 Supporting channels

Rising ambient intensity as danger climbs; light, colour and shadow shifting; a threat's
posture changing from calm to defensive — a tell (§7.3) doubles as ambient danger legibility
at no extra cost.

**[BLANK — Talon]** whether the game commits to a dedicated diegetic audio channel that is
*always* meaningful. Deep Rock Galactic's version of this
is giving dwarves no idle chatter, so that any bark at all carries system state. Note this
constrains sound design broadly, which is why it is a call rather than a detail.

### 8.4 Where have we been

**[BLANK — Talon]** the device, or combination. Note that a world which **re-rolls what it
reclaims** — zone interiors regenerating behind an advancing boundary, deliberately destroying
environmental memory — inverts half the list. Where that is true, wayfinding devices split by
lifetime:

- **Within a day** — player traces (footprints, disturbed terrain) and depletion (gathered
  things visibly gone) remain honest, and the re-roll erases them on schedule rather than
  making them lies.
- **Across runs** — a **player-authored record kept at home** survives a re-roll precisely
  because it is not in the world. The environment forgetting while the players' own marks
  remember is a design, not a gap.
- **Always** — the landmark: a home location that stays visible makes "toward home" legible by
  sightline alone. Composes with §6.1 at no cost, and a boundary converging *on* home means
  the safe direction and the readable direction agree by construction.

### 8.5 These cues are partly a stopgap — say so

There is no player failure state on `master` and no urgency feedback of any kind. Some of what
this section asks ambient cues to carry is work that the failure systems should be doing, and
pretending otherwise would let a stopgap harden into an architecture. **Revisit this section
once `BEHAVIOR-BIBLE.md` §10's Downed/Dead package ships.** If players still cannot feel
urgency once a dusk sweep is in and a persistent player record holds, that is not a
cue-tuning problem — it points at the failure systems rather than the cues.

*Scope note: re-cut 2026-07-29, de-named 2026-08-18. The build has no urgency feedback, no
failure state, and no runtime boundary to carry a dusk sweep. Current bite: none. First real
test: the first level composed with a deadline. Passing looks
like a player with their audio severed calling out the dusk sweep before it reaches them —
the same bar `INTERACTION-BIBLE.md` §8's scope note sets, applied to level state rather
than to a single consequence.*
