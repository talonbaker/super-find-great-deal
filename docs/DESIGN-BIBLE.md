# Design Bible

**Applies to:** ambiguous design decisions where a feature technically works either way,
and something has to decide which way it leans. This is different from Behavior, Mechanics
and Interaction — those describe **what counts as a defect**; this one is about **taste**,
where nothing is broken and two valid directions are competing. Everything in this file is a
preference, and the distinction between the two kinds of document is worth keeping in mind
when reading any of them.

**How to use:** when an implementing agent hits a genuine judgment call not resolved by
`BEHAVIOR-BIBLE.md` / `MECHANICS-BIBLE.md` / `INTERACTION-BIBLE.md`, check here first. If
still unresolved, surface the specific fork to Talon rather than silently picking one side.

## Established principles (carry forward)

- Mechanics that create forced social hierarchy or dominance dynamics tend to work against
  what this game is for. Asymmetric random abilities as a cooperation driver read better than
  anything that ranks or subordinates players relative to each other.
- **The commitment ladder** (ratified 2026-07-22, blocker session): every design-level
  position is `exploring` → `provisional` → `decided`, stated explicitly wherever it is
  stamped. `exploring` may be reshaped freely; `provisional` may be built on but carries
  a visible marker; `decided` requires Talon to move it. A stamp with no status is a bug
  in the doc, not a decision.
- **No decisions without substrate** (2026-07-22, Talon, stated twice in one session):
  when a mechanic's prerequisites don't exist yet — swim mechanics before there's an
  island with water, defeat rewards before a loop economy, session length before a
  playable loop — the decision is **postponed with a named trigger**, not resolved
  early. Deciding it anyway is a design bug even if the answer sounds reasonable: it
  hardens guesses into constraints on systems that don't exist. Corollary for agents:
  when surfacing a fork, first ask whether the fork is *ripe*; if its substrate is
  missing, propose the trigger, not the options.
## Starter principles (proposed defaults — confirm or override as you go)

- **Fairness & telegraphing:** danger tends to work better telegraphed before it can harm
  the player — damage from an off-screen or wholly untelegraphed source usually reads as
  unfair rather than surprising. A beat may deliberately want the jolt instead; saying which
  mode applies to a given encounter is worth doing either way, rather than defaulting to
  "fair" without noticing you chose.
- **Proportionate consequence:** punishment for player error reads best when it roughly
  matches the severity of the mistake. A harsh mechanic can be an identity rather than a
  mistake — the thing to avoid is backing into one accidentally.
- **Tone consistency:** when a design choice is ambiguous, default toward whatever's
  already established in the art/tone direction (`ART-BIBLE.md`, `PROPORTION-STYLE.md`)
  rather than introducing a new, inconsistent tone.
- **When truly unresolved:** don't let an implementing agent silently pick a side on a
  genuine values/taste call — surface the fork explicitly to Talon with the two (or more)
  options and their tradeoffs, and wait for a decision, rather than guessing and building
  on top of a guess.

This document is expected to grow — add a principle here the first time a genuine
ambiguous-taste conflict actually comes up, rather than trying to pre-fill every possible
future judgment call now.
