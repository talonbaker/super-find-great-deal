# Interaction Bible

**Applies to:** anything the player directly acts on — buttons, doors, levers, pickups,
prompts, terminals. The mirror case of `BEHAVIOR-BIBLE.md` (things that act on the player)
— this is things the player acts on.

**How to use:** read it before declaring such a feature complete, and take what applies.
**Nothing here is a rule or a gate.** These are the things players have reliably found
confusing when they were absent. Build and change freely; this is here for when something
feels wrong and it is not obvious why.

## 1. Affordance

- Interactables that look like scenery get walked past. A highlight, outline, prompt icon or
  distinct silhouette is the usual fix. Hidden and secret is a real intent — worth saying so,
  since otherwise the next person reads it as the thing being missed.

## 2. Feedback

- Feedback at the exact moment of the trigger — visual, and audio where it fits. Silent or
  delayed acknowledgement reads to the player as the input not having registered, and they
  press again.

## 3. Physical / Visual Correspondence

- Anything described with a physical verb — a button "presses", a lever "pulls", a door
  "opens" — reads best if it visibly does that. State changing internally with nothing moving
  is the shortcut that later gets reported as "it didn't work". Deliberately invisible is
  fine when it is deliberate.

## 4. Range & Timing

- Interaction range and required input (tap vs hold, proximity vs an explicit key press) are
  worth pinning rather than inheriting. An explicit interact-key press within a small, clearly
  defined radius is a reasonable place to start.

## 5. Multiplayer Contention

- Shared interactables get touched by two players at once eventually — first-wins, shared
  cooldown, or both allowed are all valid. Left undecided it resolves as whichever machine
  got there first, which is a race condition rather than a choice.

## 6. Reversibility

- State explicitly whether an interaction is one-time, a toggle, or freely repeatable, and
  whether/when it resets (on relog, respawn, timer, or never).

## 7. Interrupt Handling

- Interruptions mid-interaction (damage, moving out of range, disconnecting) are worth a
  thought — the failure mode is the player or the interactable stuck in a state neither can
  leave.

---

# Part II — Interaction Contracts

Sections 1–7 check a finished interactable. Sections 8–9 are **contracts** every consequence
and every tool verb declares up front.

Source: `docs/superpowers/research/mechanics-interactables-RESEARCH-RESULT.md`, categories 6
and 2.

## 8. Consequence Perception Scope

§2 says an interaction gives immediate feedback. It never says *to whom*. In a 1–6 player
co-op game with severable audio — designed for four (canon fact 15, Talon 2026-08-21) — that
omission is the whole problem.

### 8.1 Every consequence declares a scope

`actor_only` | `proximity` | `all_players`.

- `actor_only` — purely local (my stamina drained). The actor perceives it; nobody else needs
  to.
- `proximity` — perceivable by those nearby.
- `all_players` — **shared stakes**: the run changed for everyone. The threat is now aware. The pressure
  reached the danger line. The route out collapsed.

Scope is assigned per consequence *type*, once, not decided per firing. Unassigned is not a
default — it is an incomplete feature.

### 8.2 The redundant-channel rule

**A consequence scoped `proximity` or `all_players` whose primary channel is audio works far
better with at least one non-audio channel behind it** — a visual world cue or a UI ping.

This one carries more weight than most of the file, and the reasoning is specific to this game
rather than stylistic: the
tether mechanic **deliberately severs a player's audio**. If "the Parent is awake" is carried
only by the threat's own audio cue, then a player whose tether is cut is blind to a run-critical event
— and blind through a mechanic the game did that to them on purpose.

The distinction that makes this tractable:

| Kind of information | May be lost with the tether |
|---|---|
| Social communication between players | **Yes** — the fade to silence is content. Lean into it. |
| Run-critical world state | **No** — needs a channel that survives severance |

Proximity-voice games weaponise lost communication for tension and are right to. The error is
letting the same severance take the *game's* voice as well as the players'.

For the single-player case of this principle — a state change being legible to the player it
happened to — see `MECHANICS-BIBLE.md` §2.5. §8 is its multiplayer generalisation: §2.5 asks
whether the actor can perceive it, §8 asks who *else* must, and through what.

### 8.3 Consequences cascade

An action resolves to outcome → state change → feedback → **optional cascading behaviour
change**. A struck creature escalating from struggle to vocalise, drawing something larger, is one
consequence with a tail. The cascade is what makes interaction consequential rather than
isolated, and each link in it is itself a consequence needing its own scope.

### 8.4 Open

- **[BLANK — Talon]** whether the game surfaces any of this through UI at all, or stays
  fully diegetic (world and audio cues only). A strong tonal call. Note §8.2 constrains it
  but does not settle it: a diegetic game covers it with a visual world cue and needs no
  HUD at all.
- **[BLANK — Talon]** client-prediction posture — does the actor see the hit land instantly,
  reconciled after?

*Scope note: written 2026-07-20. Current bite: none — no consequence in the build declares a
scope today. First real test: the first `all_players` consequence, most likely the threat
waking or a shared threshold crossed. Passing looks like a playtest where one player's
audio is cut and they still call out the danger before it reaches them.*

## 9. Verb → Target → Effect

### 9.1 Effect is not a property of the tool

A tool runs **equip → target/aim → use → cooldown → effect → feedback**, and the effect is a
function of `(verb, target_type)`, resolved at use time. One stick used on soft ground digs,
on a steep slope aids a climb, on a creature strikes, on a young creature may calm instead.

The economy is one tool servicing many verbs by context. Author entries with
`/spec-interaction`.

### 9.2 Verb categories

*Re-cut 2026-07-29. The previous six-verb set (`strike`/`dig`/`climb_aid`/`pry`/`distract`/
`calm`) was authored against the jungle game and is no longer the closed canon. The new
canon was grounded in a since-retired loop design; the §9.1
law — effect is a function of `(verb, target_type)`, one tool servicing many verbs — is
unchanged and applies to all of it.*

**Committed verbs** — each has a decided referent in the loop design:

`carry` / `throw` (the only verbs in code today) · `open` / `close` (doors — open, you can
watch; closed, you can only hear; closing your own door is a choice with a cost)
· `place` (deposit what you are carrying — it only counts once placed)
· `mark` (a player-authored persistent record) · a night-commit verb (the
bedtime formality; a `CommittedAction`, never a UI lock) · `listen` / `speak`
(any one-way audio relay — one-way listening is a verb, not a HUD feature)

**Carried candidates** — survive from the old set with no committed referent yet;
authoring one via `/spec-interaction` requires naming its target and register first:

`strike` (comic register only) · `pry` · `distract` · `climb_aid` · `dig`

**[BLANK — Talon — referent superseded 2026-07-29]** The old blank here asked whether `calm`
is a tool verb or an unarmed action; its rationale cited calm as one of two exits from an
aggroed Family, and that referent died with the roster. The question underneath survives in
sharper form: **the verb roster currently has no de-escalation verb at all** — the threat of the
day was appeased by tribute, not calmed by an action. Whether any threat (an ambient creature is the
candidate) is de-escalatable in the moment is that entity's spec question, not a tool-system
default. *Trigger: the first ambient creature's entity spec.*

### 9.3 Noise-generating verbs feed the one shared channel

Every noisy verb — a thrown prop landing, a door slammed instead of closed, a `strike` —
emits into the same noise-propagation channel as a loud surface underfoot
(`BEHAVIOR-BIBLE.md` §9.1; written for Families, the channel outlives them). Not a parallel
system, not a private sound. A threat that hears the space the players are in
(its anger fed by mistakes, so noise discipline near it is a
mechanic, not a stealth garnish). Relative intensity tiers are **[BLANK — Talon]**; the
single channel is not.

### 9.4 Terrain-writing verbs need registration

`dig` and `pry` mutate terrain, which means a tool granting them is a terrain writer and
needs a row in the write-access registry (`MECHANICS-BIBLE.md` §9.3) before it is built.
Player tools are **not currently registered**. `/spec-interaction` flags this rather than
assuming it.

### 9.5 Every verb declares its feedback

Per §2 and §8: channels, and a perception scope. A verb whose effect nobody but the actor can
perceive is worth marking `actor_only` deliberately rather than arriving there by omission.

*Scope note: re-cut 2026-07-29 at `5438bf7`. Carry/throw exist in code; no other verb, no
tool system, no door interaction, no pile, no ritual. Current bite: carry/throw only. First
real test: the door pass (open/close/watch/hear) and a deposit target. Passing looks like
`open` on a door and `place` on that target resolving through the same `(verb, target_type)`
dispatch with no special-case branching, each entry naming its noise tier and scope — and a
slammed door audibly costing something a closed one does not.*
