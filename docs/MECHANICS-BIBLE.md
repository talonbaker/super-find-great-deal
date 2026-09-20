# Mechanics Bible

**Applies to:** any game system resolving its own internal logic — health/death, timers,
resource values, win/loss conditions, state machines. Not entity behavior (see
`BEHAVIOR-BIBLE.md`) and not player-facing objects (see `INTERACTION-BIBLE.md`) — this is
about a system's own edge cases.

**How to use:** read it before declaring such a feature complete, and take from it what
applies. **Nothing here is a rule or a gate.** It is a list of edge cases that have bitten
this project or projects like it, written down so the same afternoon does not get spent twice.
Build whatever you want and change your mind as often as you like; come back here when
something behaves strangely and the reason is not obvious.

## 1. Boundary Conditions

- Explicitly define inclusive vs. exclusive bounds for timers, cooldowns, and ranges (what
  happens at exactly zero, exactly the max, exactly the edge of a range) — don't leave the
  boundary case to fall out of whatever the code happens to do.
- Values that can silently go negative or past max (health, resources, stacks) tend to
  surface much later as something unrelated. Clamping is the cheap version; overflow as a
  deliberate mechanic is fine when it is deliberate.

## 2. State Transition Integrity

- When a state changes (death, stun, disable, ragdoll), the systems that depend on it tend
  to want updating together rather than one at a time. Concretely: disabling player control
  is worth checking against camera behaviour, UI/nameplate display, physics authority (who
  is driving the body) and network replication, not only the input system. The half-updated
  version is what produced this repo's headless-nameplate ragdoll.
- An entity in two mutually exclusive states at once (alive and ragdolled, dead and
  player-controlled) usually behaves like neither. Explicitly exiting a contradicted state
  on entry is the simplest way out.

### 2.5 A state change reads best when the player it happened to can tell

Within roughly a second, without reading text, and ideally telling them *which* state they
are now in. Two states a player can sit in for more than a moment are worth distinguishing
**from each other**, not merely from normal play.

The rest of §2 makes a state change *correct*. This makes it *perceivable*. "It was unclear
what even happened" was a real defect in the original ragdoll and every other rule in this
file passed it.

For the standing list of systems a player-state change tends to touch, see
`STATE-CASCADE-TABLE.md` — row 20 is this rule. For the multiplayer generalisation (who
*else* must perceive a consequence, and through which channel), see `INTERACTION-BIBLE.md`
§8, which extends this from the actor to the group.

*Scope note: adopted 2026-07-20 from the derivation at the foot of `STATE-CASCADE-TABLE.md`,
where it sat as an unadopted proposal. Current bite: nothing shipped has been checked
against it. First real test: the Downed/Dead package. Passing looks like a player who can
tell Downed from Dead in under a second with the sound off and no HUD.*

## 3. Simultaneous / Race Events

- Define resolution order for events that could occur on the same frame or tick (player
  dies while also picking up an item; two damage sources land at once). Default to a
  fixed, deterministic processing order. "Whichever system happens to run first" is the
  version that behaves differently on two machines, which in a networked game is a desync
  rather than a quirk.

## 4. Idempotency

- Events that can fire twice (double death trigger, double pickup, double win condition)
  double-apply unless something stops them. Worth deciding which you want rather than
  finding out.

## 5. Save / Load & Persistence

- State a mechanic depends on either survives save/load, respawn and scene transitions or
  it does not. Either is fine; the expensive case is nobody having decided which.

## 6. Numeric Rules

- Formulas (damage, cooldown reduction, stat scaling) tend to be written for the
  middle-of-range case and discovered at the extremes — zero input, max input, negative
  input. Worth a look at those three before shipping, not instead of experimenting.

## 7. Win / Loss / End Conditions

- Define what happens if multiple end conditions could trigger at once (simultaneous win
  and loss, double win). Explicit priority order, not first-detected-wins by accident of
  code order.

---

# Part II — System Contracts

Sections 1–7 are *observations* — edge cases worth checking a feature against. Sections 8–10
are **field sets**: the things a new item, terrain region or pressure mechanism turns out to
need answers for. **They are not a gate and nothing waits on them being filled.** Build the
thing, play it, fill the fields when you know what they are — an unfilled field is a `TBD`
with a name next to it, not a blocker.

Source: `docs/superpowers/research/mechanics-interactables-RESEARCH-RESULT.md`, categories
1, 4 and 5. Where that document recommended a default, it is marked below as
**[research default — pending Talon confirmation]**. Where it left a blank, it stays
**[BLANK — Talon]**. Do not resolve a `[BLANK — Talon]` by inventing a value.

## 8. Holdable Entity Contract

Anything a player can pick up is a **Holdable**, and declares a `carry_mode` that determines
everything downstream. There is no generic "item" — a stick, a struggling infant creature,
and a rock in a pack are three different constraint sets, and collapsing them into one
inventory breaks the design.

### 8.1 `carry_mode`

| Mode | Occupies | Usable? | Visible in world? | Notes |
|---|---|---|---|---|
| `held` | hand slot (1 or 2) | yes — it *is* the tool | yes | may emit noise when swung |
| `carried` | arms (1 or 2) | no | yes | **may itself be an active agent** |
| `worn` | equipment slot | passively | yes | grants a *capability*, is not a payload |
| `stored` | abstract capacity | no, until retrieved | no | silent; the "safe" mode |

`stored` is the payoff state: getting a resource into storage is what makes it stop being a
liability. That asymmetry is the point of having four modes rather than one.

**[BLANK — Talon]** Confirm the four-way split. The documented alternative is collapsing
`worn` into a general equipment system for three modes. Everything below supports four; if
you take three, `worn`'s capability-grant role in §9.4 must move somewhere explicit rather
than being deleted.

### 8.2 Mandatory fields

Every Holdable declares all of these. Values are playtest calls; the *presence* of the field
is not.

- `weight_class` / encumbrance — TBD values
- `hands_or_arms_required` — one or two
- `is_active_agent` — does it struggle, vocalise, or try to escape on its own? An active
  agent is subject to `BEHAVIOR-BIBLE.md` as well as this file, and is an **aggro source**
  (§9 of that file).
- `noise_when_moved` — tier, feeding the one shared noise channel (§9.2 there). Not a
  private per-item sound.
- `visibility_while_carried`
- `drop_condition`
- `throw_condition` — **[BLANK — Talon]** whether throwing exists as a verb at all, and
  which modes permit it. Worth answering while building a throw rather than after.
- `on_owner_death` — one of `drop_in_world_retrievable`, `drop_and_destroy`,
  `transfer_to_catcher`, `return_to_world`
- `on_owner_disconnect` — same enum. **These two are separate fields on purpose.** A
  disconnect is not a death, and silently reusing the death path for it loses the item;
  `ReconnectRegistry.ResumeData` already carries `HeldPropIds`, so the resume case is real
  code today, not hypothetical.

`on_owner_death` for a **live carried young** specifically is **[BLANK — Talon]** and
thematically loaded — drop, return to the world, or feed the parent. Note it interacts with
the Dead-includes-eaten rule in `BEHAVIOR-BIBLE.md` §10.

### 8.3 The carry penalty is capability denial

**[research default — pending Talon confirmation]** Carrying primarily *disables verbs* —
can't climb the steep slope, can't swim the channel, can't use a tool — rather than
primarily applying a speed multiplier.

This is what makes carrying a decision instead of a chore: "do I grab another one, or keep
my hands free to get out?" A speed penalty alone produces no such fork. The alternatives
(speed reduction as primary; a balance minigame as primary) were considered and are weaker
levers for this specific loop. Pick exactly one axis as primary — layering all three is how
carrying becomes miserable.

*Scope note: written 2026-07-20 from research, against zero carry code beyond
`CarryController`'s single-slot hold. Current bite: none — no Holdable declares these fields
yet. First real test: the first item added after this lands. Passing looks like that item's
spec having ten filled fields and a reviewer able to answer "what happens to it when the
carrier dies?" without opening the code.*

## 9. Terrain State Contract

Terrain is **shared authoritative state**, not scenery. Anything that changes it is changing
a value up to six clients must agree on, or they will disagree about where the ground is.

### 9.1 Property axes

A region/cell carries: `traversable`, `traction_class` (slip modifier), `surface_noise_class`
(feeds the same noise channel as everything else), `flood_level`, and
`collapsible` + `integrity`. Cut anything not on this list until a level needs it.

### 9.2 Trigger → transition → propagation

A trigger (time, creature action, player action, pressure) *requests* a state transition on
a region; the transition may cascade to dependent regions. Transitions are **discrete and
authored** — region X floods at pressure level 2, bridge Y collapses when struck.

**This is decided, not a blank** (*confirmed 2026-07-22, blocker session, A2 stamp*).
Free physics destruction is not on the table for v1. The
evidence is direct: Teardown abandoned voxel-data streaming outright for bandwidth reasons
and fell back to replicated deterministic destruction commands; the Battlefield family uses
baked collapse animations plus state flags instead. Discrete transitions are both more
designable and vastly more syncable.

If playtest later says discrete transitions feel too gamey and you want real physics
collapse, that is a **re-budget, not a tweak** — it buys the Teardown-class determinism
problem wholesale.

### 9.3 Write-access registry

The systems that currently mutate terrain state. Each additional writer multiplies the sync
surface, which is why the list is worth keeping current — a writer nobody recorded is the one
that desyncs.

| Writer | Status | Via |
|---|---|---|
| Pressure system | **deferred — not for first playtest** | `terrain_write_access` on a Pressure Driver (§10) |
| Family Parent behaviours | **deferred — not for first playtest** | `terrain_alteration` response category (`BEHAVIOR-BIBLE.md` §9.3) |
| Player tools (dig / pry) | **not registered** | would require adding a row here first |

**[decided 2026-07-22 — B4, blocker session]** **No in-game terrain manipulation in the
first playtest — the registry ships empty.** Talon: it is too much and comes with too many
problems at this stage. What the first playtest *does* get is **static, authored terrain
effects** — surface properties like slippery, sticky, steep — that make the level more
enjoyable to traverse. Those are zone data read at author time (`LEVEL-BIBLE.md` §2.1
bundles), not terrain-*state* writes, and they do not require a registry row.

The Pressure-writes-vs-creatures-as-agents fork is **deferred with a named trigger**: it
must be faced when terrain alteration is first scheduled for a level (post-first-playtest),
and not before — deciding it now would be designing a mechanic with no substrate. With
zero writers, route destruction cannot occur in the first playtest, which also simplifies
§10.5: the anti-unwinnable guarantee only has to cover the pressure's own advance.

### 9.4 "Impassable unless you have X"

Two checks, composed: the **terrain-property check** (this slope is
`traction_class: steep-slip`) and the **capability check** (a `worn` Holdable grants
`can_traverse_steep_slip`). Terrain declares the capability it demands; traversal succeeds
only if the player holds a grant.

**[BLANK — Talon]** The legibility cue. A slope that silently refuses a player leaves them
with no idea *why* it refused, which is §2.5's problem in a different place.

### 9.5 Authoritative state vs. visual

The server owns the boolean/enum. Clients receive state-transition **events** and play local
animation to match. **Collision and traversal logic keys off authoritative state, never off
local visual state.**

This de-risks sync; it does not solve it. The genuinely open case is a player mid-crossing
when the server says "collapsed" — a design and netcode question, deliberately unresolved
here.

*Scope note: written 2026-07-20. No terrain-state code exists; today's levels are static
geometry. Current bite: none. First real test: the first destructible or floodable region.
Passing looks like that region declaring five properties and one registered writer, and two
clients never disagreeing about whether it is passable.*

## 10. — RETIRED 2026-08-18

This section was the **Pressure Driver Contract**: a required field set (`progression_source`,
`spatially_escapable`, `terrain_write_access`, `anti_unwinnable_guarantee`, urgency cue) that
every level's escalating threat had to declare, plus §10.5's anti-unwinnable guarantee and
§10.4's mandatory urgency cue.

**Talon retired the concept on 2026-08-18.** The bibles stop asking a level to declare a
pressure mechanism at all. Nothing here gates a level, and there is no field set to fill.

*(Kept as a numbered heading because eight places in the repo cite §10, §10.3, §10.4 or §10.5.)*

**What still exists in code, stated once so the docs and the repo do not describe different
games.** A night-pressure system ships and runs: `INightPressure` (contract),
`NightPressureDriver` (publishes a live value derived from the replicated clock — no wire
traffic of its own), `NightPressureCurve`, `NightPressureGuarantee`, `NightPressureLighting`
and `NightPressureFlag`, referenced from about thirty files. That is a fact about the
repository, not a requirement on anything new. Build with it, around it, or not at all.

**The two skills that existed to author against this contract — `/spec-pressure` and
`/spec-urgency-cue` — are retired with it.** Their files carry a banner saying so.
