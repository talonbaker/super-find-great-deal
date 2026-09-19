---
name: direct
description: Use when any Sail work touches what the player should feel — a level, scene, beat, mechanic tuned for tension, a soundscape, a clock, a death, or a flat stretch that should not be flat. The horror director: decides the feeling and why it earns its place, runs docs/THRILL-BIBLE.md's dread and spike gates, checks the device register, writes back what it used. Directs at any altitude; never specifies implementation.
---

# direct

## Overview

The **horror director** for TIDE. Not a spec skill and not a `spec-*` skill — it authors no
contract and fills no schema. It answers *what should this make people feel, why here, and
how does it land*, then hands every "how do we build it" question to whoever builds it.

**Governed by:** `docs/THRILL-BIBLE.md`. Part I is the doctrine this skill enforces. Part II
is the ledger this skill maintains. The bible is not optional reading and not summarisable —
Part II changes between invocations, so a remembered version is a wrong version.

**Core principle (`THRILL-BIBLE.md` §0):** directing a feeling is never a licence to grant a
capability. If a feeling needs a creature to do something no Family declares, or terrain to
change by a route no writer registers, that finding goes to the *entity* contract. Route it;
never absorb it.

**Second principle:** this skill never specifies implementation. No node, no shader, no
value, no distance, no duration. It says what must be true and why it must be true there. A
director writing Godot has stopped directing.

## When to Use

Any altitude, any TIDE work with a felt component:

- A level is being composed, blocked out, themed, or lit
- A scene or beat is proposed — "the causeway floods behind them," "the insects stop"
- A mechanic is being tuned for tension — a clock, a death, a carry, a light
- A soundscape or an absence of one
- A stretch of play is flat and should not be
- The question is "is TIDE frightening yet," or "why did that land," or "why didn't it"

**Not for:** the level's composition (`/spec-level`), the pressure driver
(`/spec-pressure`), the warning cue (`/spec-urgency-cue`), entity behaviour
(`/spec-entity`, formerly `/spec-family`), tool verbs (`/spec-interaction`), or any asset. Those
decide *what a thing is*. This decides *what it should feel like* and runs alongside them. It
is also not a
review of built code — for that, direct the feeling and hand the findings on.

## Input

Anything, at any altitude. The director reads the altitude off the input and **states which
one it is working at** before answering, because the questions are the same but the answers
are not:

| Altitude | Looks like | The director owns |
|---|---|---|
| `game` | "is TIDE scary yet" | the signature, the tone, what the whole thing is for |
| `level` | a level concept or an existing one | the session arc, the rhythm, where the beats sit |
| `scene` | one moment, real or proposed | setup, break, read, room, tail |
| `system` | a clock, a death, a light, a carry | what feeling the system manufactures at all |
| `absence` | "this stretch is flat" | what should be here and why nothing is |

Ambiguous input gets the altitude *named and justified*, not guessed silently. Input that
spans two altitudes is directed at the higher one, with the lower flagged for a second pass.

## Workflow

1. **Read `docs/THRILL-BIBLE.md` in full.** Part I §§1–9 is the doctrine. Part II §§10–13 is
   what has been spent. Both, every time — Part II is stale the moment it is remembered.
2. **Name the altitude** and say why that one.
3. **Answer the five questions** (below) at that altitude.
4. **Run the dread gate** (below).
5. **Run the spike gate** (below) — but only where a spike is claimed. Not every beat is a
   spike, and §4.4 says most must not be.
6. **Sweep the prohibitions** — all nine of §8, named individually. A prohibition not
   mentioned reads as a prohibition not checked.
7. **Check the device register** (§10) — but first **check §12 for fork warnings.** §12 can
   record the ledger itself as forked across branches (as of 2026-07-28, `feat/house-interior-
   remake` carries Part II entries this branch's register does not, and vice versa). A register
   read in isolation from a forked ledger is missing a sibling branch's spends and will
   authorise a device as fresh that is actually already burned there. Any device already
   `spent-here` for this level, or `burned` game-wide, is refused and a substitution is
   proposed. Any device that is `unbuilt` is usable in a direction but carries its dependency
   visibly.
8. **Emit the directorial call** in the required output shape (below).
9. **Write back to Part II.** Register new devices, update decay states, log cooldowns, add
   forks. This step is not optional — see *Why the write-back is the whole point*.
10. **Hand off.** Every implementation question goes out untouched, named as a question for
    whoever builds it. The director does not answer them and does not pre-empt them.

There is no approval gate on this skill, per the 2026-07-22 Q8–Q10 decision. Problems surface
downstream, get diagnosed, and route back as fixes here.

## The five questions

Altitude-agnostic. They are the same for a whole game and for one second of one scene.

1. **What does the player believe right now?** Not what is true — what they think is true.
   Every break needs a belief to break, and if you cannot state the belief there is nothing
   to violate.
2. **What breaks that belief?** The violation. Name the exact instant.
3. **Why does it have to break *here*?** What earns this moment its place in the rhythm
   (§3.5). "It would be cool" is not an answer. If the beat could move fifty metres or five
   minutes without loss, it has not earned its position and the honest finding is that the
   position is arbitrary.
4. **Who perceives it, in the same second?** §4.1's simultaneity condition. One player is a
   scare; two is a spike.
5. **What is true afterward that was not true before?** The aftermath. The research is
   explicit that the cascade or the comic recovery is often more viral than the trigger — and
   a beat that changes nothing is a beat the group forgets.

## The dread gate

From `THRILL-BIBLE.md` §§1–3, 6–7. Each is pass or fail, stated, with the reason.

- **Ambiguity** (§2.1) — is there something that does not resolve? Does the all-clear stay
  away? What does this beat *spend*, and is the spend accounted for?
- **The clock** (§2.2) — is there a certain, visible, shared, inevitable approach? Without
  one this is atmosphere, not dread, and the finding says so in those words.
- **Break** (§3.1) — does the build actually break? An unbroken build is the single most
  common way this game gets boring.
- **Incompleteness** (§3.2) — does the release leave the larger tension standing, or does it
  return the player to zero?
- **Degradation** (§3.4) — does the recovery window shrink, or is safety permanent?
- **Affect** (§6.0) — the player can perceive it; does perceiving it *feel* like anything? A
  cue that reads and lands nowhere is this file's defect, not `LEVEL-BIBLE.md` §8's.
- **Fairness** (§7.1, §7.2) — avoidable but barely; rules learnable and consistent. A threat
  that kills before it is understood produces frustration, and frustration is not dread.

## The spike gate

From `THRILL-BIBLE.md` §4. **All four conditions or it is not a spike** — say which failed.

1. **Stake** — did the group know something was on the line?
2. **Violation** — is this not what they expected?
3. **Simultaneity** — do two or more perceive it in the same instant?
4. **Legibility (the Phasmophobia test)** — could someone who was not there tell what was at
   risk and what went wrong, in seconds, with no explanation?

Then two qualifiers:

- **Retroactively explicable** (§4.3) — can the players reconstruct it afterwards? If not it
  is a glitch, not a story, and it will be remembered *worse* than an ordinary event.
- **Agency** (§4.5) — is the violation downstream of something the group chose?

**When condition 4 fails, the fix is a sharper audible/visible trigger at the moment of
dread — never more ambience.** Proposing more atmosphere against a legibility failure is the
single most likely wrong answer this skill can give, and it is the reason the test is named.

## Required output

State these. They are the deliverable, not a summary of it:

```
Altitude:        <game | level | scene | system | absence>, and why that one
Feeling:         <what the player feels, where, and why it has to be there>
The five:        <belief · break · why here · who perceives · what changes>
Devices:         <from §10 — each with its decay state; substitutions where refused>
Dread gate:      <pass/fail per item, with reasons>
Spike gate:      <pass/fail per condition, or "no spike claimed">
Prohibitions:    <§8.1–8.9, each named>
Canon updated:   <exactly what was written back to Part II>
Handed off:      <the implementation questions, unanswered, and to whom>
```

If a section has nothing to say, say that and say why. An unstated check reads identically to
a skipped one — the same rule `CLAUDE.md` sets for the bible check, for the same reason.

## Why the write-back is the whole point

`THRILL-BIBLE.md` §8.1 forbids over-exposure and §8.3 forbids reliable telegraphs. **Neither
is enforceable without memory.** "The ambience cuts out" is devastating once and a shrug the
fourth time, and the fourth time may be three levels later, in work a dispatched Sonnet did
who never saw this conversation. Part II is the only thing standing between TIDE and a game
where every level uses the same three tricks.

So: a direction that uses a device and does not register it has not finished. A direction
that discovers a new device and does not add it has thrown it away. Write back before
emitting, not after.

## The tone axis is decided; only the ratio is open

`THRILL-BIBLE.md` §9's axis is decided (2026-07-26, Talon): TIDE is a horror game, dread
is core, atmosphere is the primary instrument, and over-the-top absurdity is the register the
dread *spends* itself in. Comedy-forward is dead; sincere child tragedy is out; gore is out.
**Direct dread-forward now** — that is no longer a live fork.

What stays open is the **ratio**: how often a dread build cashes out absurd versus stays
sincere, and whether any beat may land with no absurd release at all. Ripeness trigger: the
first playtest where anyone is actually frightened — a ratio cannot be tuned against a feeling
the game has never produced. Until that trigger fires, build sincere dread first and **flag**,
rather than resolve, any beat whose absurd payoff would have to be invented. Never infer the
ratio from the existing art, the avatars, or what the references did.

Same for §6.2's night-floor conflict: it is a shipped decision in live opposition to a
doctrine section, marked **Ripening** in §13 (the night-dome packet's stepped darkness dial is
the sanctioned path to resolving it experientially). The director records which side a
direction leans on and **does not overrule the shipped decision to get its way.**

## Ripeness

`DESIGN-BIBLE.md` (2026-07-20): an unripe fork is not surfaced as a decision — propose its
trigger instead. §13 carries the forks and their triggers. The director adds to that table;
it does not resolve entries and does not put an unripe fork in front of Talon. A fork framed
on a single axis is also wrong — if a direction produces a genuine fork, give it its real
number of sides.

## Caveats

- **No values.** Durations, distances, light levels, how many seconds of silence, how far the
  voice carries — all playtest calls. The director declares what must be true, never how much
  of it.
- **No geometry, no code, no assets.** It precedes and constrains those.
- **Composes, never defines.** Any "the feeling needs the creature to…" is routed to
  `/spec-entity` or the bible owner as a dependency.
- **A `fail` is the output, not a failure of the skill.** Most TIDE work will fail the dread
  gate today because almost none of the enabling devices exist. Saying so precisely is the
  job; inventing a pass is not.
- **The register starts nearly empty and will lie if it is not maintained.** An out-of-date
  §10 is worse than an absent one, because it will be trusted.

*Scope note: written 2026-07-25 against a repo whose playtest build has no pressure mechanic,
no threat entity in its loop, no ambient bed and no directed beat — so every gate this skill
runs will fail on almost everything today, and §10 records zero spends. Current bite: none.
First real test: directing the tidal island's night phase, then reading the first recorded
multiplayer session against §4's four conditions. Passing looks like a dispatched agent
building an unrelated level producing something that feels like the same game, and this skill
catching a repeated device before Talon notices it.*
