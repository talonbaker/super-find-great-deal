# Behavior Bible

**Applies to:** any entity with autonomous movement, AI, or a "chase/attack/flee/patrol"
verb in its design.

**How to use:** read it before declaring such a feature complete, and take from it what
applies. **Nothing in this file is a rule and nothing here is a gate.** It is a list of
things that have gone wrong in this repo before, written down so nobody has to rediscover
them — several of these describe bugs that actually shipped. Knowing what broke last time is
useful; being forbidden from trying something is not, and this file does not do the second
one. Iterate freely; come back here when something feels off and the reason is not obvious.

Don't ask permission to act on anything below, and don't treat any of it as a reason to
refuse work. Surface a question to Talon only for a genuinely ambiguous value.

## 1. Spawning & Placement

*Place things wherever you want. This section is about what has surprised people afterwards.*

- Spawning inside geometry or outside the playable bounds usually reads as the entity simply
  not existing, which is a confusing way to find out something went wrong.
- An entity sealed behind geometry the player cannot reach is unreachable rather than
  hidden — it has shipped in this repo, and it looked like the creature was missing.
- When a chosen spawn point turns out to be invalid, falling back to the nearest valid point
  and logging it is easier to debug than defaulting to world origin (0,0,0) or silently not
  spawning at all. Both of those have happened and both looked like nothing happened.

## 2. Orientation & Facing

- An entity moving toward a target generally wants to face its movement direction, or the
  target, every frame rather than only at spawn. A chasing entity facing backwards has
  shipped here and read as broken rather than stylised. "Moves without turning" is a real
  style (crab-walk, turret-locked) and worth saying out loud when it is the intent, so the
  next person does not read it as the same bug.

## 3. Movement & Pursuit

- Chase speed reads better as a **per-creature** value than as a global
  faster/slower-than-player rule (amended 2026-07-22, B5; the old "slightly slower than the
  player" default over-weighted flat-ground speed). Raw speed is one number on flat ground —
  escape difficulty comes from the whole context: terrain, tools in hand, carry state, the
  creature's domain. A pursuer that is neither always nor never outrunnable tends to produce
  the most interesting chases; one that resolves identically on every terrain has usually
  collapsed to the single number.
  A defending parent tends to want to be genuinely hard to escape without tools or terrain
  use, even where it is defeatable.
- A pursuit with no give-up condition — no leash range, no timeout, no line-of-sight loss —
  tends to end with the creature walking off the edge of the map. That has shipped here. Any
  of the three works; the point is that unbounded pursuit is rarely what anyone meant.
- Clipping through walls to reach a target reads as the creature cheating, which is usually
  worse than it failing to reach you.

## 4. Detection & Aggro

- Detection triggers (proximity, line-of-sight, sound, or a combination) are worth stating
  rather than leaving implicit. Proximity AND line-of-sight together is a reasonable
  starting point when nothing else suggests itself.
- Aggro should trigger once per encounter, with an explicit reset condition (e.g., losing
  the target for N seconds), not re-trigger every frame the condition is true.

## 5. Contact / Collision-Triggered Effects

- Contact effects driven by a distance check or an animation cue rather than an actual
  collision land when nothing visibly touched the player. This shipped here as headbutt
  damage firing at range, and it read as the game hitting you for no reason.
- Knockback originating from the real contact point and direction reads as being struck;
  knockback from the entity's centre or a fixed vector reads as being shoved by physics.

## 6. Group Behavior

- Multiple identical entities near the player should not all trigger simultaneously
  without coordination (avoid instant multi-hit pile-ons) unless that's an intentional
  design (a swarm/mob mechanic) — state which applies.

## 7. Animation–Gameplay Sync

- A gameplay effect tied to an animation lands best keyed to the frame it belongs to.
  Firing at animation start is the common shortcut and it visibly desyncs the effect from
  the picture.

## 8. Death / Defeat of NPCs

- An entity that keeps moving, attacking or dealing contact damage after it is defeated
  reads as the death not having happened. The player-side version of this shipped here — a
  dead player's ragdoll still under player control, nameplate floating over a headless
  body — and it is the kind of thing that is funny once and confusing after that.

---

# Part II — Behavioural Contracts

Sections 1–8 are checks against a finished entity. Sections 9–10 are **contracts**: the field
set a creature family or a player-failure state declares before it is built. `TBD` with a
named decider is an acceptable cell; blank is the finding.

Source: `docs/superpowers/research/mechanics-interactables-RESEARCH-RESULT.md`, categories 3
and 7.

## 9. Family Contract

A **Family** is the parameterised "parent defends young" structure. Animal, fruit-bearing
trees, and bees all instantiate it; none of them hardcodes it. Historically authored with
`/spec-family`, superseded 2026-07-29 by `/spec-entity` for any live (post-pivot) entity —
this section stands as the reference contract `/spec-entity` ported its reusable machinery
from, not as a live authoring path.

A Family is three parts: **Young** (harvestable, cannot fight back), **Parent** (defender),
and a **provocation channel** — the noise/proximity/harvest signal connecting them. There is
no fourth part, and the channel is not optional: a Parent that aggros by any private
mechanism of its own is off-contract.

### 9.1 The provocation channel is the shared noise system

The cries of a struggling carried young, a tool strike, and a footfall on a loud surface all
emit into **one** noise channel — the same one declared by `noise_when_moved`
(`MECHANICS-BIBLE.md` §8.2), `surface_noise_class` (§9.1 there), and tool verbs
(`INTERACTION-BIBLE.md` §9). A noise event carries an intensity and propagates along
traversable space rather than through solid geometry at full volume. Any creature whose
hearing radius it intersects gains aggro.

Two noise systems is the failure mode to avoid here. If carrying a creature and hitting a rock
wake the mother through different code paths, they will diverge and only one will get tuned.

### 9.2 Young capture-response ladder

A Family declares which tiers its young use and in what order. These are categories; the
thresholds are playtest calls.

- **Struggle / wriggle** — physical resistance; slows the carrier, may break free
- **Vocalise / alert** — emits into the provocation channel. **This is the primary aggro
  driver**, and the reason a carried young is an aggro source rather than cargo.
- **Go limp / give up** — passive past a threshold; noise drops; the "safe to carry" state
- **Quiet under player action** — responds to the `calm` verb by reducing vocalisation

The ladder gives carrying its shape over time: loud and dangerous at first, safe if you wait
or work for it. **[BLANK — Talon]** the order and tier boundaries per family.

### 9.3 Parent response categories

A Family declares which subset it uses. It does not get to invent a fifth.

- **Direct pursuit** — hunts the player or carrier
- **Terrain alteration** — destroys, floods, or collapses routes. **Requires registration in
  the terrain write-access registry** (`MECHANICS-BIBLE.md` §9.3); a Parent may not touch
  terrain without a row there.
- **Summoning / alerting** — wakes or calls others; a cascading aggro amplifier
- **Area denial** — makes a zone dangerous without pursuing

### 9.4 Aggro scope: per-family, with spatial propagation and decay

**[decided 2026-07-22 — A2 stamp confirmed]** Aggro belongs to a family and propagates
spatially, ramping and cooling rather than latching.

The alternatives and why not: per-creature is the most emergent but unreadable to a full
six-player party and heaviest to sync; per-zone flattens the ecosystem into a room state; per-level is
one global alarm and deletes the quiet corner, which is where the whole stealth-adjacent
texture of the game lives. Per-family is the readable middle and matches the fiction.

**[BLANK — Talon]** whether propagation is instant across the family or chains at a speed.

**Cross-family propagation: there is no global aggro bus.** Waking one family does not raise
another family's alertness through any shared flag. Families are independent threat states on
this shared contract.

This is not a stylistic preference. Rain World — the closest ecosystem analog to what this game is
building — gives each creature its own relationship table against a shared schema and has no
global bus; its only cross-cutting propagation is a per-species *player reputation* that is
deliberately invisible in play. Shadow of Mordor actually built genuine propagating
multi-faction shared state, and **cut it in pre-production**: its design director described the
resulting hierarchy UI as looking "somewhat like a Christmas tree" and the system as
"overcomplicated." A hidden global alarm is the specific failure mode both avoided.

If a Family's action should visibly affect another Family, model it as a **diegetic physical
consequence, not an abstract alert flag** — the mother's terrain alteration splashes, the
splash reaches the hive, the bees respond to *the splash*. That routes through the existing
provocation channel (§9.1), stays legible, and needs no new machinery.

**[decided 2026-07-22 — confirmed]** No global aggro bus; diegetic physical consequence is
the only cross-family channel. The alternative — deliberate cross-family alerting — remains
a contract change with two cited cautionary precedents, not a spec-level knob.

### 9.5 Defeatability is a set of per-family capability flags

Amended 2026-07-22 (decision Q1, superseding the 2026-07-21 A2 amendment and the
2026-07-20 "a Parent can never be defeated" original). The boolean `can_be_defeated` is
**retired**. In its place, a family declares **capability flags**: `can_be_killed`,
`can_be_trapped`, `can_be_stunned`, `can_be_evaded`. Families declare which apply; **none
are mandatory** — zero flags is legal, and an absent flag means that capability is not
designed for that family (equivalent to `false`). The death animation maps to
`can_be_killed`. Decision log:
`docs/superpowers/status/2026-07-22-blocker-decisions-Q1-Q10.md` (Q1); A2's spirit —
Talon verbatim, "everything should be killable if done in the right way by the players" —
survives as the *designability* of these flags, not as a blanket mandate.

The rule has three parts:

1. **Capabilities are per-family declarations, not combat stats.** A Parent is still not
   a combat encounter and still has no ordinary health pool — no generic damage verb
   reduces one to zero. Declaring `can_be_killed: true` means an earned, non-trivial
   path to killing this creature exists in its design ("done in the right way");
   "attack it more" is never that path. What defeat *yields* — resources, points, area
   access, nothing — is deliberately undefined until the loop economy exists (Q2):
   player motivation emerges from play, not design constraint. A `defeat_method` field
   may be declared when the design earns one, but is no longer schema-required.
2. **Evade and calm remain the expected primary exits.** The kill/trap/stun capabilities
   are committed, costly alternatives — plans a group executes, not fallbacks a player
   stumbles into. A calm verb that is fiddly or unreliable still makes the game
   unplayable, because calm and evasion are the only *routine* exits from an aggroed
   family.
3. **Death is not fancy.** `can_be_killed` does not buy a death set-piece; the cheapest
   legible presentation is the bar (see `ART-BIBLE.md` §6.5).

Pikmin, the closest functional analog to this loop, allows killing the parent, which
scatters the young and ends the encounter. This game keeps that release valve but tolls it:
it exists per-family, it costs commitment, and it is never the path of least resistance.
Two consequences survive from the previous version of this section, with their rationale
updated:

- **Route destruction still has no *reliable* counter-play through the aggressor.** A
  kill/trap/stun path may be unavailable, unaffordable, or unexecutable in the moment —
  which is why `MECHANICS-BIBLE.md` §10.5's anti-unwinnable guarantee remains required
  rather than optional. Defeatability does not substitute for it.
- **The combat axis exists only as declared.** "The answer is to fight it" is on-contract
  exactly where a Family spec declares `can_be_killed` (or trap/stun), and off-contract
  everywhere else.

### 9.6 De-escalation is a race, reversible until a point of no return

`calm` applies negative agitation over time against a rising escalation timer. Below the
threshold in time, the threat resets; past the point of no return, it does not. A point of no
return is what gives the race stakes — a fully reversible threat tends not to have any.

**[direction decided 2026-07-22 (F4.4), details deferred]** **Proximity voice is a calm
input, per-creature:** a fussy young creature can be calmed by a player simply talking near
it; some creatures are voice-calmable, some are not — declared per creature rather than as a
global rule, and not as a limit imposed on the player. Whether `calm`
also targets Parents, and every mechanical detail beyond "voice soothes the young,"
is deliberately deferred (no substrate yet — see `DESIGN-BIBLE.md`, no-decisions-without-
substrate). Trigger: the Family runtime's calm implementation.

*Scope note: written 2026-07-20 from research, against no family code — every creature that
exists is a standalone entity with private aggro. Current bite: none. First real test:
the first real family. Passing looks like two families sharing the contract with no shared
base class doing family-specific work, and a player able to read "she is waking" without
being told.*

## 10. Player Failure State Contract

The mirror of §8. §8 is about a defeated NPC stopping everything at once; this is about a
failing **player** transitioning everything at once. Same shape, opposite subject, and the
harder of the two, because a player keeps a camera, a UI, a voice and a network identity
after failing.

### 10.1 Two states, decided

*Confirmed decided 2026-07-22 (blocker session, A2 stamp) — no longer provisional.*

**Downed** — the player can nudge their own ragdoll: inch along, reach for a friend, crawl
out of danger. Recoverable. Explicitly not called death.

**Dead** — no control. Reserved for causes where the fiction makes it self-evident: falling
from height, water and lava, and **being eaten** (torn to pieces is not recoverable).

*Water as a Dead cause is **provisional** (2026-07-22, B7): it kills outright for now
because swim/drown mechanics have no substrate yet — deciding them before the island
exists would be premature. Revisit trigger: when water traversal becomes buildable work
(swim-away-then-drown-eventually is the expected direction, not a promise).*

Decided 2026-07-20, and this **overrides the research's recommendation** of
downed → rescuable-with-cost → dead-if-abandoned. Recorded here with its reasoning so it does
not get "corrected" back:

- The research's spectrum was respawn / rescue-by-teammate / run-ending / partial-soft-loss.
  Downed is a fifth option it did not list — **self-recovery under impairment**.
- Rescue-with-cost makes a *teammate* a hard dependency for recovery. This game severs audio by
  design (tether), and category 6 of the same research warns against exactly this: a
  run-critical outcome gated on a communication channel the game deliberately cuts. The
  recommended model fights the tether; self-recovery does not.
- Control is therefore not taken during normal play, so the never-lose-control note at
  `SandboxAvatar.cs:23-27` survives intact and needs no exception.

Teammate assistance works well *added* on top as an accelerator; as the mechanism it makes a
teammate a hard dependency, which is the thing this section was written to avoid.

### 10.2 The transition is atomic

Entering either state updates every dependent system in one commit —
`MECHANICS-BIBLE.md` §2. **The system list is `STATE-CASCADE-TABLE.md`, every row**, and
it is not restated here on purpose: two lists drift, and that one is derived from the code
rather than remembered. Fill every cell `CHANGED (how)` or `UNCHANGED (why safe)`.

Four rows are known to be load-bearing for this contract specifically and are called out only
because the table's own derivation flagged them as *missing API, not configuration*: physics
authority (exactly one writer per tick), client prediction (ragdoll physics cannot be
replayed — this state is server-authoritative at full RTT), camera (`SandboxCamera` has no
detach), and carried items (row 8, which defers to `on_owner_death` /
`on_owner_disconnect` in `MECHANICS-BIBLE.md` §8.2 — this contract does not redefine them).

### 10.3 Both states read best legible, and distinguishable from each other

Per `MECHANICS-BIBLE.md` §2.5: within about a second, without text, the player can tell which
of the two they are in. Distinguishable from *each other*, not merely from normal play — a red
tint meaning "something bad" and nothing more leaves the player guessing.

Teammates are a second audience with their own scope requirement — see
`INTERACTION-BIBLE.md` §8.

### 10.4 Open

- **[BLANK — Talon]** what a Downed player can do beyond nudging: call out, spectate,
  nothing else? Intersects proximity voice and the tether.
- **[BLANK — Talon]** whether being caught by a Parent is always Downed or run-ending. This
  is a **per-Family flag rather than a global rule** — declared in the Family spec. Worth
  keeping consistent with §10.1: a Parent that eats produces Dead, one that pins produces
  Downed.
- **[BLANK — Talon]** the Downed → Dead transition — nudging yourself into lava.

*Scope note: written 2026-07-20. No player failure state exists on `master`; there is no
ragdoll code there either, though it is recoverable from `feat/sorting-grounds`. Current
bite: none. First real test: the Downed/Dead package itself. Passing looks like a filled
20-row cascade table before implementation starts, and both states shipping without a "we
forgot system X" follow-up.*
