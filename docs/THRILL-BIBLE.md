# Thrill Bible

**Applies to:** what the player should *feel*, why they should feel it there, and how a
feeling becomes a moment worth retelling. Not what a thing does — that is
`MECHANICS-BIBLE.md`; not how it behaves — `BEHAVIOR-BIBLE.md`; not how the player acts on
it — `INTERACTION-BIBLE.md`; not how a level is arranged — `LEVEL-BIBLE.md`. This file is
about the affect those four produce when someone plays them with three friends at 11pm.

**How to use:** invoke `/direct`. The skill reads this file in full, applies Part I as
doctrine, checks Part II for what has already been spent, and writes back what it used. Every
section here is either a rule the director enforces or a ledger the director maintains. Only
surface a question to Talon for a genuine fork — never for a value.

---

## 0. What this file is, and what it is not

**The genre is settled: this is a horror game.** Decided by Talon 2026-07-26 (§9). It is meant
to be genuinely scary, principally through atmosphere, with dread as a core aspect rather than
a coat of paint. Absurdity is how the horror pays out, not a hedge against having to produce
fear. Nothing else in this file is optional on the grounds that TIDE "might end up a comedy."

**This is a doctrine file with a ledger attached.** Part I is what TIDE believes about fear
and thrill. Part II is the memory of what TIDE has actually done with those beliefs — which
devices exist, where each has been used, and which are burnt out. Part I changes rarely. Part
II changes every time the director works.

**Source:** `docs/superpowers/research/horror-dread-RESEARCH-RESULT.md` and
`docs/superpowers/research/viral-moments-RESEARCH-RESULT.md`, both received 2026-07-25. Where
the research recommended a default it is marked **[research default — pending Talon
confirmation]**; where it left a fork it stays **[BLANK — Talon]**. Do not resolve a
`[BLANK — Talon]` by inventing a value. The research files are the source of record; this
file is the decision. Where they disagree, read both.

**This file composes contracted entities; it does not define new ones.** Same rule as
`LEVEL-BIBLE.md` §0. If a feeling requires a creature to do something no Family declares, or
terrain to change by a route no writer registers, the finding is in the *entity* contract, not
here. **Directing a feeling is never a licence to grant a capability.** Wanting the forest to
seem to have intent does not give the forest intent — it constrains how the entities that do
have intent are revealed.

**This file never specifies implementation.** It says what must be true and why. It does not
pick a value, a node, a shader, a distance or a duration. Those go to whoever is building it.
A director who starts writing Godot has stopped directing.

**Why this file exists.** `LEVEL-BIBLE.md` §8.5 says its ambient cues "are partly a stopgap"
and §5.1 defers session length entirely; `/spec-level` reports that §8 and §5.4 have nowhere
in the manifest to land. The Level Bible governs whether a player can *perceive* the state of
the world. Nothing until now governed what perceiving it should *feel like*. That is the hole
this file fills, and the boundary is exact — see §6.0.

---

# Part I — Doctrine

## 1. The target: dread, not fear

Four states, routinely conflated, that demand different designs:

| State | Trigger | Duration | Design instrument |
|---|---|---|---|
| **Startle** | sudden sensory spike | ~1s, reflex | release only, never build (§8.2) |
| **Fear** | a present, identified threat | acute, ends with the threat | the peak beat (§3) |
| **Anxiety** | diffuse, unspecific apprehension | ambient, directionless | not a target — it has no shape |
| **Dread** | an *approaching, uncertain* bad outcome | sustained, directional | **the primary target** |

**Dread is anxiety plus certainty of arrival.** "Something might go wrong" is anxiety.
"Something *will* go wrong, and I can't tell what, and I can't stop the clock" is dread. The
certainty is what gives it direction, and the ambiguity is what stops it resolving. Remove
either and it collapses — a certain *known* threat is just a fight, and an uncertain
*unscheduled* threat is just unease.

**Creepiness is the companion state** and is defined in the research as an evolved response to
**ambiguity about the presence of threat** — being unable to predict how something will
behave, or to read its intent (McAndrew & Koehnke 2016; the disrupted-mentalization refinement,
Kjeldgaard-Christiansen & Clasen 2023). Creepiness is what the world produces between spikes.
It is cheap, it does not require an entity, and it survives being in a group — which makes it the correct register for the supported player range (canon §2.13) outdoors.

**TIDE's target state is dread and creepiness, punctuated by brief fear, released by
laughter.** Sustained terror is exhausting and players disengage from it. Startle alone is
forgettable within a minute.

*Scope note: written 2026-07-25. TIDE has never been playtested for fear of any kind — the
Steam playtest build ships no pressure mechanic and no threat entity in its loop. Current
bite: none. First real test: the first session anyone reports not wanting to keep going.
Passing looks like a playtester describing the feeling with a word from the dread column
unprompted.*

## 2. The engine: ambiguity under a certain clock

Two ingredients, and the whole file is downstream of them.

**2.1 Ambiguity is the fuel.** The all-clear must never arrive. Anticipation of harm engages
threat circuits more strongly than the harm itself, and people will accept a *worse* outcome
sooner purely to end the waiting (Story et al. 2013: 71% of 35 volunteers). Every design
choice that resolves uncertainty spends fuel. Some spending is necessary — see §3's release —
but it is always a spend and the director accounts for it.

**Corollary — the world must seem to have intent you cannot read.** Not intent it *has*;
intent it *seems* to have. Traces, timing, and absence do this. An entity that appears,
behaves legibly, and can be categorised does the opposite, which is why §8.1 forbids
over-exposure.

**2.2 The clock is the certainty.** A pressure driver that is visible, shared, monotonic and
inevitable is what converts ambient unease into directional dread. It is also the single
cheapest source of it: no entity, no AI, no animation. `MECHANICS-BIBLE.md` §10 owns what a
pressure driver *is*; this file owns why it is the emotional keystone and refuses to let a
level ship its feeling on anything else.

**A level with no clock is not dreadful. It is atmospheric.** Those are different products
and the difference is measurable in whether players hurry.

**2.3 Cost-to-know.** The strongest single device in the researched corpus is Alien:
Isolation's motion tracker — a tool that reduces your uncertainty *and* increases your danger,
because using it makes noise the threat can hear. **Any device that trades safety for
information is worth more than the same information given free.** A light that reveals and
attracts. A voice that coordinates and betrays. Prefer these; they make ambiguity a player
decision instead of a designer withholding.

*Scope note: 2026-07-25. TIDE's rising tide exists on the core-loop stack but is absent from
the shipped playtest build; the day/night cycle (`CycleDriver`) is the only clock currently
running. Current bite: partial — §2.2's test can be run against the cycle today. First real
test: the tidal island with its pressure driver live. Passing looks like players visibly
changing their behaviour at a threshold nobody told them about.*

## 3. The arc: build, peak, release, recovery

**3.1 Release is mandatory.** Unbroken tension does not accumulate — it normalises. The
player stops being tense and cannot be made tense again cheaply (the *Amnesia: A Machine for
Pigs* failure). Constant assault produces the same numbness from the other direction. Every
build must break.

**3.2 Release is incomplete.** The break resolves *this* tension and leaves the larger one
standing. A false alarm, a near-miss, an escape into a sanctuary that is itself degrading.
A full all-clear returns the player to zero and the next build has to start over.

**3.3 In co-op, laughter is a legitimate release** and the most valuable kind, because it is
also the shareable artefact (§4). Zeekerss's stated design for Lethal Company is that fear is
"followed right up with laughter from your friends" — the game is "about laughing at death."
The director does not fight the group's humour; it schedules the moments that provoke it.

**3.4 Recovery must degrade.** A sanctuary that stays safe kills the clock. Safe windows get
shorter, safe places get fewer, or safety costs something that runs out. `LEVEL-BIBLE.md` §5.2
already establishes that the pacing lever is *frequency, not amplitude*; this file adds that
the valleys must also *shorten*, not merely alternate.

**3.5 Rhythm is the director's real work.** Spacing, not intensity. The research is
unanimous — L4D's Director, Amnesia's post-death environment changes, the Theseus pacing
thesis. Two beats too close and neither lands; too far apart and the build decays.

*Scope note: 2026-07-25. No level in TIDE has authored beats; playtests to date are
open-ended. Current bite: none. First real test: the first level directed with `/direct`.
Passing looks like a session whose tension, if plotted, has visible valleys — the same test
`LEVEL-BIBLE.md` §5.4 sets for pacing, measured on affect instead of threat count.*

## 4. The spike: what makes a moment worth retelling

A **spike** is the beat that releases a build and is simultaneously the thing people clip. It
is not a jump scare and not merely an exciting event.

**4.1 The four conditions. All four, or it is not a spike.**

1. **Stake** — something the group cares about is on the line, and they knew it was.
2. **Violation** — what happens is not what they expected. Surprise is *defined* as
   expectation violation, and violations are encoded into memory more strongly than expected
   events.
3. **Simultaneity** — two or more players perceive it in the same moment. This is what makes
   the reaction contagious rather than individual.
4. **Legibility** — a person who was not there can tell what was at risk and what went wrong,
   within seconds, without explanation.

**4.2 The Phasmophobia test.** Pure atmospheric dread famously "never looks as scary in the
clips as when I'm playing." A beat that fails condition 4 may still be excellent horror and
will still fail to travel. **This is the same defect as §3.1's numbness seen from outside:**
a build with no break is both un-scary over time and un-clippable in the moment. When a beat
fails the test, the fix is a sharper audible/visible trigger at the moment of dread — a light
change, a collapse, a disappearance — not more ambience.

**4.3 Surprising but retroactively explicable.** Expectancy-violating events become *less*
memorable when they are unexplainable. "The causeway flooded behind us while we argued" is a
story. "We were teleported into the sky" is a bug. Every spike must be reconstructible by the
players afterwards — that reconstruction *is* the retelling.

**4.4 Spikes are rare.** The *Breakaway* filter — rare, hard, consequential — holds. A session
affords roughly one true spike, and inflation is the fastest way to devalue every one of them.
**[research default — pending Talon confirmation]** one spike per session as the ceiling; a
second is permitted only if the first was a false alarm.

**4.5 Player agency is required.** A spike caused by nothing the group did reads as arbitrary
and produces no story, because there is nothing to have done differently. The violation must
be downstream of a decision — pushing deeper, splitting up, taking the heavy thing.

**4.6 Alignment.** The strongest spikes align an audio event with a visual one in the same
instant. Proximity voice supplies the audio half for free, which is why §5.1 ranks it first.

*Scope note: 2026-07-25. TIDE has produced no spike of any kind and has never been recorded
in a session. Current bite: none — this section is entirely forward-looking. First real test:
the first recorded multiplayer session. Passing looks like two players reacting in the same
second, and a clip a stranger understands with the sound on and no context.*

## 5. The group: fragile cooperation

**5.1 Proximity voice is the highest-leverage instrument in the game.** It is the delivery
system for emotional contagion — the mechanism by which one player's panic becomes everyone's
involuntarily — and it makes every moment self-documenting. It is also, per the research, the
one ingredient common to every viral co-op success of the last three years. TIDE already has
it (`scripts/voice/`, 3D attenuation with a PA route).

**5.2 Contagion favours small groups.** Modelling (Nakahashi & Ohtsuki 2018) indicates
sensitivity to environmental cues *weakens* as group size grows, i.e. smaller groups panic
together more reliably. The supported range (canon §2.13) is in the right band. The most-frightened player is an
asset, not an outlier — they are the vector.

**5.3 The alpha-hero problem.** A group removes isolation, which is the usual engine of
horror. The counter is not to weaken the group: make the group **genuinely stronger together**,
then apply pressures that force it apart. Separation is only frightening if togetherness was
worth something.

**5.4 Information asymmetry is the seed of panic.** Not knowing what is happening to the
others is worse than knowing something bad is. Anything that makes what each player perceives
differ — position, light, distance, who is carrying what — is a dread multiplier.

**5.5 The subtraction of a voice is a device in its own right.** A teammate mid-sentence, then
nothing, is the interpersonal form of §6.3's wrong silence and lands harder than any noise.

**5.6 Failure must be content.** Every viral co-op game makes dying the funniest outcome, not
the most punishing. That requires low friction after death — the dead stay audible and stay in
the room — and a consequence small enough that "we almost died" never becomes "we wasted an
hour."

*Scope note: 2026-07-25. Proximity voice ships and works; it has no threat consequence, no
occlusion by geometry, and there is no afterlife channel. Nothing in TIDE currently forces
separation. Current bite: §§5.1–5.2 hold today; §§5.3–5.6 describe nothing that exists. First
real test: the networking playtest. Passing looks like players spontaneously shouting across
distance, and voices audibly thinning as they part.*

## 6. The world: dread without an entity

**6.0 Boundary with `LEVEL-BIBLE.md` §8.** §8 owns **legibility** — whether a player can
perceive the state of the world and the pressure on it. This section owns **affect** — what
perceiving it should feel like. A cue that cannot be perceived is a §8 defect; a cue that is
perceived and feels like nothing is a §6 defect. Neither file restates the other's rules;
`LEVEL-BIBLE.md` §1 is explicit that two copies of a field drift until only one gets enforced.

**6.1 Prospect and refuge.** People instinctively want to see without being seen (Appleton
1975). Denying prospect while offering only *false* refuge — cover that also conceals what is
coming — is the base state of a frightening outdoor space. Clearings that expose. Hiding
places that blind.

**6.2 The night reversal is the strongest environmental device available, and TIDE currently
declaws it.** Enclosed shaded space is *desirable* by day and *threatening* after dark — the
same geometry inverts its emotional valence as the clock runs. A darkness clock therefore does
something no other pressure can: it re-signs the entire map without moving a single mesh.

> **⚠ Live conflict — flagged, not resolved.** `DayNightSky.cs` holds a deliberate night
> ambient floor (`MinAmbientEnergy`, decision §6d#25) so that night stays *navigable*. That
> decision and this section want opposite things: navigability requires the dark be survivable
> by sight, the reversal requires it cost sight. Both readings are legitimate — a dark you
> cannot navigate is frustration, a dark that costs nothing is set dressing. **This is a fork
> for Talon and the director will not overrule a shipped decision to get its way.** Neither
> "quietly raise the floor" nor "quietly ignore the reversal" is an acceptable resolution.
> *Ripeness trigger: a playtest in which anyone reports the dark as a threat rather than a
> palette. Until then this is unripe — do not surface it as a decision.*
>
> **Ripening note, 2026-07-26.** §9's tone axis is now decided as dread-forward with atmosphere
> named as the primary instrument, which weights this fork toward the reversal — but does not
> settle it, because a dark nobody can navigate is still frustration rather than dread. The
> resolution path is deliberately experiential: the night-dome / passage-of-time packet ships a
> stepped darkness dial (level 1 = the shipped §6d#25 floor, levels 2–3 progressively darker)
> so Talon decides this by standing in each one after dusk. The director still does not pick.

**6.3 Wrong silence.** Withdrawing a sound the player has stopped noticing is stronger than
adding one. It requires an established ambient bed to withdraw — the bed is the setup and its
absence is the payoff, which makes this one device that must be *invested in before it can be
spent*. Real predators produce this cue in real forests, which is why it needs no explanation.

**6.4 Accumulating wrongnesses.** Dread compounds from small dissonances that individually
explain nothing: tracks that circle, a structure that should not be there, remains, an
*absence* where something should be. Each raises alertness slightly and none resolves. This is
§3's arc at micro-scale and it is the cheapest dread in the file.

**6.5 Traces, not exposition.** Introduce the thing without ever showing it. Damage, remains,
markers, paths worn by something. `LEVEL-BIBLE.md` §7.3's "one tell, first exposure is
ordered" governs *which* tell and *when*; this section governs that the tell is a trace rather
than a sighting.

**6.6 Colour temperature is a threat channel.** Cool reads as distance, unfamiliarity and
threat; warm reads as life and intimacy. A warm place gone cold is a statement.
`ART-BIBLE.md` owns the palette; this file only claims that the *direction* of a shift is
meaningful and must not be arbitrary.

*Scope note: 2026-07-25. TIDE has no ambient bed, so §6.3 is currently impossible to execute —
it is an investment, not a technique. §6.2 is in live conflict with a shipped decision.
§§6.1/6.4/6.5 are unexercised. Current bite: none of this is implemented anywhere. First real
test: the jungle island's first atmosphere pass. Passing looks like a player naming something
that unsettled them that was not an entity and not a sound cue anyone authored as a scare.*

## 7. Difficulty, fairness, and the shape of a threat

**7.1 Avoidable but barely.** A threat that kills before the player understands it produces no
fear, only frustration — the Cursed Companions finding: their proximity-triggered monsters
killed players "before he knew anything happened... he didn't have time to feel scared," and
retuning them to trigger on being seen or heard first restored the dread. **The player needs a
little control, not much.** The agony is the point; the death is not.

**7.2 Rules must be learnable and consistent.** Dread comes from ambiguity about *presence*,
never from arbitrariness about *rules*. A threat whose behaviour changes without cause is not
mysterious, it is broken, and players correctly read it as unfair. This is the line between
dread and frustration and it is not subtle.

**7.3 Weakness is a design requirement.** Reliance on the group, limited means, slowness. A
capable player is not a frightened one.

**7.4 Nothing here softens `MECHANICS-BIBLE.md` §10.5.** The anti-unwinnable guarantee has no
valid TBD and no amount of directed dread justifies a sealed level. A director who asks for a
guaranteed-loss beat is asking for a bug.

*Scope note: 2026-07-25. TIDE's only threat entities (goat, hoarder) are off the current loop
and were never tuned against §7.1. Current bite: none. First real test: the first threat
placed in a directed level. Passing looks like players dying and blaming themselves.*

## 8. The prohibitions

Violating one of these is a finding, not a style choice. Each is drawn from both research
files independently.

**8.1 Over-exposure.** Showing the threat too often or too clearly collapses ambiguity into a
known quantity. Once categorised, it is a mechanic, not a dread.

**8.2 Startle as build.** A jump scare is a *release* and a weak one. Used as the engine it
desensitises, breaks immersion, strips agency, and reads as cheap. "Predictability is the
enemy of fear."

**8.3 Reliable telegraphs.** If a cue always precedes a threat, players learn to relax in its
absence — the cue has taught them when they are safe. Break the correlation with false alarms
and unsignalled events. (Note the tension with `LEVEL-BIBLE.md` §8.2's *urgency* cue, which
must stay reliable: a warning that a state is changing is a fairness contract and stays
honest. A hint that something is about to happen is a dread device and must sometimes lie.
These are different cues and must not be the same channel.)

**8.4 Scripted set-pieces.** Anything that fires identically every time stops surprising on
run two and reads as artificial immediately. Build the possibility space; let the players
supply the drama. **Emergence is still authored** — this is not licence to design nothing, it
is an instruction about *where* the design goes.

**8.5 Illegible chaos.** Noise with no discernible stake or cause is tedium, not thrill, and
fails §4.1 conditions 1 and 4 simultaneously.

**8.6 Unrelenting tension.** See §3.1. The most common way a horror game becomes boring.

**8.7 Permanent safety.** A sanctuary that never degrades, or a group that is always strongest
together with no counter-pressure, ends the clock early.

**8.8 Streamer-first design.** Moments engineered for the camera read as inauthentic, and an
inauthentic reaction is not contagious. Design for the players in the room; the clip is a
byproduct of a real reaction or it is worthless.

**8.9 Punitive failure.** If losing costs enough to sting, "we almost died" becomes "we wasted
an hour" and the release valve inverts into resentment.

## 9. Tone position — **AXIS DECIDED 2026-07-26 (Talon). Calibration still open.**

> **This game is a horror game.** It is meant to be genuinely frightening, and it is meant to
> be frightening **principally through atmosphere**. Dread is a core aspect of it, not a mood
> applied over something else. Over-the-top absurdity is the register the horror *spends*
> itself in — not a substitute for producing fear in the first place.

Talon, verbatim, 2026-07-26:

> *"I want dread to be a core aspect of this game. I don't need gore. I want horror though. I
> would like you to write to make sure it is known the game is meant to be scary through
> atmosphere and all other things. To be a horror game."*

And, earlier the same day, the register it is delivered in:

> *"I would very much like horror. That should be the game. I would just like it to be over the
> top absurd rather than dread and gore."*

**Read those two together, because either one alone gets misread.** Of the four positions this
section used to list, the decided one is **dread-forward** — with the release taking the form
of over-the-top absurdity rather than aftermath laughter. *Evil Dead II*, not *Hereditary*, and
not Lethal Company either: a beat is allowed to be genuinely frightening and absurd at the same
instant, and the excess is the source of both.

**What this settles, and the director may now act on:**

- **Dread is doctrine, not an option.** §1 stands as written. §2's ambiguity-under-a-clock
  stands. §6's entire environmental apparatus stands and is now the *primary* instrument —
  Talon named atmosphere first and by name. §6.3's ambient bed is no longer a nice-to-have
  investment; it is on the critical path, because wrong silence is unavailable without it.
- **Comedy-forward is dead.** So is "horror decoration over a comedy." A build in which nobody
  is ever frightened has failed on its primary axis, and the director should say so plainly
  rather than grade it on the comedy.
- **Gore is out**, permanently and by preference, not by rating anxiety. Payoff currency is
  excess and wrongness. Likewise **sincere tragedy over a player's death is out** — players die, the game never asks anyone to grieve it (see the 2026-07-26 prep-loop design doc §B1).
- **Deliberate dissonance is not foreclosed** and is arguably now the house style: the cute
  cute player surface (canon §1.2) against sincere dread is the *Evil Dead II* seam, not a
  contradiction of it. It stops being a live *alternative* and becomes a technique.
- **Tonal drift by phase is not foreclosed either** — the day/night cycle is still the obvious
  axis, and it now serves a decided direction instead of hedging between four.

**What is still genuinely open — do not infer it:** the **ratio**. How often a dread build
cashes out absurd versus stays sincere, and whether any beat may land with no absurd release at
all. *Ripeness trigger, unchanged: the first playtest in which anyone is actually frightened.
A ratio cannot be tuned against a feeling the game has never produced.* Until then the director
builds sincere dread first and flags — rather than resolves — any beat whose absurd payoff it
would have to invent.

---

# Part II — The living canon

**This half is a ledger, and it is only worth what the last person to write in it put there.**
Its purpose is to make "you have already used that" survive a context reset, a new session, or
a dispatched Sonnet who has never read this conversation. A director that does not write back
is a director with amnesia, and the §8.1/§8.3 prohibitions become unenforceable.

## 10. Device register

A **device** is a reusable technique that produces dread, a spike, or both. Each entry
declares what it is, what it produces, where it has been used, and its decay state.

**Decay states:**

| State | Meaning | Director's obligation |
|---|---|---|
| `unbuilt` | doctrine supports it; nothing exists | may be proposed; flag the dependency |
| `fresh` | built, never used | free to use |
| `seeded` | used once, still potent | may reuse in a different level |
| `spent-here` | used in this level | do not repeat *within that level* |
| `burned` | used often enough to be predictable | retire, or reinvent so the violation returns |

| Device | Produces | §  | State | Built by | Used in |
|---|---|---|---|---|---|
| Wrong silence (ambient bed withdrawal) | dread | 6.3 | `fresh` — **the mechanism now exists and has never been fired.** Changed from `unbuilt` on 2026-08-08 by the sound-integration packet, which harvested PR #73's `AmbientBed` (a continuous non-positional day/night bed, plus `Withdraw`/`Restore`) onto the campfire trunk and wired it into the played camp world. That merge is a **deposit, not a spend**: nothing in it authors a cut, a dropout or a withdrawal beat, and `Withdraw` has no caller outside `AmbientLab`'s F8 dev toggle. Spending it still requires `/direct` and an uncaused moment. The prior entry's reasoning stands unchanged and is preserved here because it is what the bed is *for* — Directed 2026-08-07 (campfire lighting, #152): the fire's crackle (`Sfx.Crackle` + `SparseSfxEmitter`, **both authored by #152 — they did not previously exist anywhere in the repo, contrary to what #152's own brief assumed**) is the repo's **first bed investment** — a continuous-reading, positional, player-maintained sound the group will stop noticing because it is always there when they are safe. That is the setup §6.3 has been blocked on since 2026-07-25. **The crackle stopping is explicitly NOT this device and must not be logged as a spend:** the fire going out is a *legible, caused, player-visible* event, and §6.3 requires withdrawing a sound whose absence has no explanation. A cause defuses it. What the campfire buys is the bed; the withdrawal still needs a wider bed and an uncaused moment to spend it on. **Enlarged 2026-08-09 by packet 1f (the looping audio path) — a second deposit, and still not a spend.** 1f gives the bed its first *continuous positional* layer (the fire's body, held open on a reserved voice rather than rented per-crackle), which is the wider bed the row above says the withdrawal was waiting on. `Withdraw` still has exactly one caller in the repo, `AmbientLab`'s F8 dev toggle, and 1f adds none. **One new refusal the enlargement creates, and it is easy to trip:** the nearest-K rule means a fire *stops being audible when you walk away from it*, and a hard cull boundary would be an unexplained silence — i.e. this device fired by a distance check, on a schedule, for free, several times a night, which is how the strongest row in the file gets burned by an optimisation. **The cull must therefore fade, never cut**, and the fade must be slow enough to read as distance rather than as an event. A withdrawal that costs nothing to trigger is not a device, it is a bug with good taste. | `vfx-audio-sync` | — (setup under construction: camp, #152, 1f) |
| Night reversal (dark re-signs the map) | dread | 6.2 | `seeded` — WP-N1 (2026-07-26) built the physical instrument (a converging wall of darkness, 2 draw calls, plus the 3 stepped darkness levels above, switchable live at runtime) specifically so §6.2 can be decided by experience rather than argument. **First played 2026-07-26 (Talon, hoodlab):** reported as "creepy and very nice… extremely effective" from inside the dark looking out — the §6.2 ripeness trigger firing (see §13). Same playtest killed the device's one §8.7 violation: the front used to park outside the house walls, leaving a permanently lit halo that never degraded; it now runs to zero, so night owns the whole exterior and the only refuge is *indoors*. **The reversal's substance is still unbuilt**: the dark changes light/fog/adjustment and nothing else — "the dark re-rolls what it eats" (loop-v1 design §5) is deferred, so being caught still costs nothing and the build has no break (§3.1, §4.1 stake). **Amended 2026-08-28 by the light title screen (MENU-1) — a DEPOSIT on the day side, not a spend.** The 2026-08-07 menu entry recorded "there is no day version of this frame to invert"; there is now, and it is the highest-repetition surface the game has. A player meets a bright, hazy, safe reading of this world before every session for the life of the build, which is the cheapest possible way to install the day valence this row's whole mechanism inverts. Nothing is withdrawn on the menu and the §6.2 fork is untouched. **That deposit was WITHDRAWN one day later, 2026-08-29 (MENU-2), and the row is worse off than before it was made.** Talon reversed the menu to a night frame, so the highest-repetition surface in the game now shows the NIGHT reading instead of the day one — the deposit is not spent, it is gone, and the cheapest day valence the repo had is no longer available. Recorded as a loss, not argued: his direction outranks a ledger deposit. §6.2's fork is still untouched and `NightAmbientFloor` was not moved by either pass. | `vfx-lighting` | hoodlab (dev-lab only, not a played level); camp (L10, #113, wired 2026-08-06, not yet played — see §12); the light title screen (day-side deposit, 2026-08-28, **WITHDRAWN 2026-08-29 — see MENU-2 in §12**) |
| Rising tide as visible clock | dread | 2.2 | `unbuilt` on master — exists on the core-loop stack, absent from the playtest build | — (mechanic, `/spec-pressure`) | — |
| Day/night cycle as clock | dread | 2.2 | `seeded` — directed 2026-07-27 as the house interior's only clock. WP-N1 (2026-07-26) added cross-run escalation (day 1 short night, ~0.25 of the cycle → day 5 long night, ~0.50) and a 3-step darkness dial (level 1 = the shipped §6d#25 navigability floor; levels 2–3 progressively darker), headed-captured on day 1 and day 5. Still atmosphere alone in the exterior — see §2.2's own dread-gate note below. | `vfx-lighting` | house interior; hoodlab (dev-lab only, not a played level); camp (L10, #113, wired 2026-08-06, not yet played — see §12) |
| Open sanctuary inverted by dark | dread | 6.1, 6.2 | `seeded` — directed 2026-07-27, house interior | `vfx-lighting` | house interior |
| Doorway as proscenium | dread + spike | 6.1, 8.1 | `fresh` — exists by geometry, never fired. **Re-checked and re-refused 2026-08-28** (BT-10, the TV portal): walking *through* a screen is not the same act as a framed view *held* through an opening, and the TV's charge is that you are already elsewhere before you have looked at anything. The reserved EPIC 2 beat is untouched. | — (level geometry) | house interior (cut, unfired) |
| Stairs/doors that lie about length or destination | dread + spike | 2.1, 4.3 | `unbuilt` — parked, see `docs/design/stairs-as-portals.md`. **Deliberately NOT spent by the 2026-07-27 house pass**, and **deliberately not spent again 2026-08-28 by BT-10** — the nearest miss this row has had. That row's lie is *quantitative*: a route the player has already read AS a route misreports its length or its far end. The TV portal's lie is *categorical* — the object was never read as a route at all, so there is no claim about length or destination for it to break. Recorded as a near-miss rather than absorbed; the device remains whole for the stairs. | — (mechanic + level) | — |
| **The object that is a door (a prop that is a route, to a room that is nowhere)** | creepiness (§1) | 1, 2.1, 4.3, 6.4 | `unbuilt` → **directed and built 2026-08-28 (BT-10, the TV portal, Bubble Test)** → **`burned` in `BubbleTest` 2026-08-29 (LEVEL-4): five televisions onto five rooms at Talon's request, which categorises the wrongness and is §8.1. Game-wide the device is UNCHANGED and available — BT-10's harness-is-not-a-game ruling still governs. See the §12 entry "Five doorways instead of one".** New row; the anti-inflation check against five adjacent entries is in the §12 entry and all five miss it. The device is that **a piece of set dressing turns out to be a way in**, and what it opens onto is not somewhere the map contains — the destination has no approach, no exterior and no place in the world's geography, so it cannot be re-found by walking. Its affect is the withheld all-clear (§2.1): the player is returned, nothing acknowledges the trip, and the prop is still sitting there doing what it was doing. **Constraints, all four load-bearing:** (a) the destination must be *leavable at will and immediately* — a room you can be held in is a trap, not a wrongness, and §7.4's anti-unwinnable rule has no exception for atmosphere; (b) the way out must be **out of frame on arrival and unmissable after one turn** — turning is the cost, searching is a stuck player; (c) **nothing in the destination may animate in response to the player's arrival**, which is what separates this from §8.2 startle and keeps the register ordered rather than disordered; (d) the transition is **local to the traveller** — a watcher given a cue would be handed an explanation they did not earn, and the asymmetry (§5.4) is the free half of the effect. **Decay note, ruled explicitly:** BT-10 spends it in `BubbleTest`, a movement/networking harness and not a level of the game, so the state is `spent-here` for that world and the device stays available game-wide — a test-harness spend is not a game spend, and a later director must not read this row as burnt. **Amended 2026-08-30 (W7-1) — a FIFTH CONSTRAINT, and no change of state.** Talon's note 8 moved four of the five televisions onto summits (22.810 m, 34.782 m, 41.200 m, 60.789 m); the state stays `burned` in `BubbleTest`, because altitude does not de-categorise a categorised wrongness. What the move exposed is that constraint (a) stops meaning anything above ground level: **(e) the way out must return the traveller ONTO the surface they left, not below it.** A return that lands one metre outside a 5 m pad 41 m up is technically leavable and is in fact a 41 m fall used as an exit — §8.9, and the climb taken back. `RoomReturnOffset` shipped at a 4.2 m diagonal and put the returning player 3 m past the edge on three of the four; W7-1 cut it to 1.98 m for that reason, and `--tvportal-selftest` now measures every return landing on its own pad. Constraint (e) binds any future use of this device at height, in any world. See the §12 entry "Four doorways moved onto summits". | — (level geometry + one script) | Bubble Test (BT-10, LEVEL-4, W7-1) |
| Proximity voice falloff | dread + spike | 5.1 | `fresh` — shipped, never used as a device. **Amended 2026-08-14 by the radio-cord pass (NIGHTJOBS-B) — still not a spend, and the amendment is about what the falloff *means*.** Falloff has shipped since before this file existed and has never been made legible as a *rule*, because nothing has ever been exempt from it: a law with no exceptions is indistinguishable from a limitation of the technology, and players read it as "the game's voice chat is short-range," not as "distance costs you your friends." A cord that carries voice between two endpoints regardless of range is the first exemption, and an exemption is what turns a limitation into a law. The row stays `fresh` because nothing is built and no beat fires on it; what the cord contributes is that the *day* it exists, falloff becomes a thing players can lose rather than a thing they merely have. **One refusal follows and it is easy to trip:** the cord must never widen `VoiceRange` or scale attenuation — it must be a **separate route** that is either carrying or not carrying, because a cord that merely stretches the falloff curve makes the break a fade, and a fade is not §5.5. | — (`scripts/voice/`) | — |
| Teammate voice cut-out | spike | 5.5 | `unbuilt` — **blocked: no afterlife channel** → **the block is not lifted, but it is no longer the only door. Amended 2026-08-14 by the radio-cord pass (NIGHTJOBS-B); a deposit under construction, NOT a spend.** This row has sat blocked since 2026-07-25 on a single unexamined assumption: that a voice cuts out because its owner *died*, which needs an afterlife channel nobody has built. A comms line that the world can sever produces the identical perceptual event — a teammate mid-sentence, then nothing — **with nobody dead, nobody kept, and nothing to grieve**, which is also the cheapest possible compliance with the clip test (canon §0.8) — the old register law's "nobody is ever taken" half is retired (canon §3) and the clip test is its durable half. The severance is bilateral and instantaneous by construction, so §4.1's simultaneity condition is satisfied *structurally* rather than by an author placing two players in a room; that is rare enough in this file to be worth naming. **Three constraints are constitutive, not tuning. (a) The break must have a moment.** Bare silence fails §4.1 condition 4 outright — the Phasmophobia test is not passed by an absence, and §4.2's rule applies verbatim: the fix is a sharper audible artefact at the instant of severance, on **both** ends, never more ambience. That artefact is the **line's** sound and never the **cause's**, or §8.3 has been handed a perfect alarm. **(b) At least one cause of a break must be indifferent** — weather, deadfall, a bad route you chose — because if only the thing that eats you can cut the line, then a dead line reliably precedes an attack and the players have been given a §8.3 telegraph that makes the game *safer* the more it is used. **(c) Repairing the break may never explain the break** (§3.2): you restore the channel and learn nothing about what took it. | — (voice system, not `vfx-*`) | — (the carrier is the radio cord, directed 2026-08-14, not built) |
| Accumulating wrongnesses | dread | 6.4 | `unbuilt` | `vfx-disturbance`, `vfx-corruption` | — |
| Route loss behind the group | spike | 4.5 | `unbuilt` — `LEVEL-BIBLE.md` §4.3 decided the rule, nothing implements it | — (level/terrain) | — |
| Cost-to-know instrument | dread | 2.3 | `unbuilt` **as an instrument — but the trade itself now exists in the game for the first time.** Amended 2026-08-09 by the panic-drop pass (R4). §2.3's row has always described a *tool* you use (the motion tracker) whose use costs safety, and that tool is still unbuilt; `vfx-proximity`'s continuous-sensing machinery is untouched. What R4 adds is the same trade with **no tool at all**: standing up out of prone is the only way to learn whether the watcher is still there, and it is exactly the act that makes you visible. Recorded as an amendment rather than a spend because the row's own instrument is what is `unbuilt`, and marking it built on the strength of a posture change would make the register lie about what exists. The device it actually produced has its own row below (*The blind hide*), and that row is the one to read. **Amended 2026-08-14 by the star-fix pass (NIGHTJOBS-B) — the row's first genuine INSTRUMENT candidate, directed and not built.** R4 gave this row the trade with no tool; a celestial bearing gives it the tool, and the tool's cost is not invented for it — it is already paid by two shipped directions meeting. A star fix is readable **only where the sky is open**, and canon 4 already says an away-from-light player is *"silhouettes against sky"*: the clearing is simultaneously the only place the instrument works and the only place the player is the most legible object on the map. That is Alien's motion tracker with the world supplying both halves and no gadget authored — the purest form this row has ever had a candidate for, and it costs nothing to build because both halves already exist. **Three constraints are constitutive, not tuning. (a) The fix must be an EVENT, never a readout** — a discrete thing you take by standing somewhere and looking up, never a value that persists on screen once you walk off. **(b) The instrument that carries it under the trees is the player's own memory, and nothing else.** No retained bearing, no last-known arrow, no map, no breadcrumb: the moment a fix survives the walk into the canopy as data, this row's cost has been refunded and §8.7 has been handed a permanent safety made of information — the exact failure *Position without judgment* was written to prevent. **(c) The sky is never modulated by threat state.** No star goes out because something is near; that would fuse the fairness lane and the dread lane in one channel and lose both (see *Position without judgment*, constraint (a)). | `vfx-proximity` | — (the *instrument* is unbuilt; the trade is hosted by R4's panic drop; the star fix is the first instrument candidate, directed 2026-08-14, not built) |
| Forced separation | dread | 5.3 | `unbuilt` — **leaned on but genuinely not spent** by the campfire fetch (2026-08-07, #152). The wood fetch *invites* separation (somebody goes, somebody stays) and produces §5.4 asymmetry for free, but it does not **force** it — the whole group can walk out together, and §5.3's precondition is unmet anyway: togetherness is not yet worth anything, because no threat exists. It becomes the real device only if the pile costs more than one trip can carry in the time available, so splitting is the only solution. That is a value the director may not pick. **Amended 2026-08-13 by the eel kit — the ledger's first genuinely FORCED instance, and still not a spend.** Four entries (the fetch, the lake, the panic drop, the glow stick) each recorded this row as *invited, not forced*, because in every one of them the group could simply stay together. A chain does not ask: it takes every body inside one circle and throws each on its own bearing, and for the length of the tumble nobody is standing where they chose to stand. That is the physical act this row has never had a host for. It is **not** a spend for the reason the row itself gives, unchanged and now on its fifth restatement: §5.3's precondition is that *togetherness be worth something*, and it still is not — nothing threatens the party, and the eel's effect on creatures is an empty stub (canon 7). What the kit contributes is the mechanism, sitting there and unpaid-for, waiting for the day separation costs anything. **Amended 2026-08-14 by the radio-cord pass (NIGHTJOBS-B) — the first payment against the PRECONDITION, which is the thing five previous amendments each said was missing, and still not a spend.** Every prior entry failed on the same clause: *"§5.3's precondition is that togetherness be worth something, and it still is not."* Nothing in the repo has ever made *being together* pay a dividend, so *being apart* could not cost one. A comms line that survives distance is the first mechanism that pays that dividend — with the cord intact, a party spread across 300 m of woods is still one party, and canon 13's frontier push stops being a solo errand with a walk attached. **The cord does not force anyone apart and this row stays `unbuilt`**; what it does is make apartness a *state with a value attached* for the first time, so that when something finally forces it, there is something to lose. Note the sequencing this implies and do not invert it: the cord must be laid, relied on, and taken for granted before a single break is worth anything, which makes this the rare device whose setup is measured in nights rather than seconds. | — (level/mechanic) | — |
| Fog as sightline denial | dread | 6.1 | `seeded` — built 2026-07-26 as the *daylight side* of the night dome, answering Talon's playtest note that "the LIGHT should also fall like the circle does." The lit pocket's own reach now contracts with the front's radius, so prospect (§6.1) is withdrawn continuously through the dusk sweep instead of the world merely dimming on a global phase curve. Note the register entry is the **coupling**, not fog itself: fog keyed to a clock is set dressing; fog keyed to the approaching front is the clock made spatial. Reusable elsewhere only where something is actually approaching. | `vfx-lighting` | hoodlab (dev-lab only, not a played level); camp (L10, #113, wired 2026-08-06, not yet played — see §12) |
| Colour temperature as threat channel | dread | 6.6 | `seeded` — the dusk sweep's hot-orange→plum→indigo grade is the warning channel (loop-v1 design §5, "golden hour IS the warning") and Talon's playtest confirms it lands ("very nice golden hour"). Deliberately **not** touched by the 2026-07-26 revision pass — it is the one part of the device already working, and re-tuning it to buy something else would spend a landed effect. | `vfx-lighting`, `vfx-corruption` | hoodlab (dev-lab only, not a played level); camp (L10, #113, wired 2026-08-06, not yet played — see §12); Sleeping Cabin interior (refined 2026-08-08 — a warm hearth accent inside a cold collapsing ambient, per ART-BIBLE §3's C1 reconciliation; the ambient statement itself untouched, so this is a lean, not a re-spend — see §12) |
| Persistent scarring / dead ground | dread | 6.4, 6.5 | `unbuilt` | `vfx-corruption` | — |
| A world that breathes | dread | 2.1, 3.5 | `unbuilt` | `vfx-pulse` | — |
| Per-player sensory asymmetry | dread → spike | 5.4 | `unbuilt` → **first carrier built 2026-08-09 (packet 1f), deposit not spend.** `AmbientBed`'s own doc records the debt this row was owed: its two continuous layers are non-positional, so "a non-positional bed sounds identical wherever a player stands, and it carries none of §5.4's asymmetry." 1f's looping-emitter mode is the first *continuous positional* layer in the repo, so for the first time two players standing in different places at night are hearing measurably different worlds — one near a fire and inside its body, one three ranks out and hearing only the nearest few. The asymmetry now exists as a **property of the mix** rather than as an effect somebody has to author. It is registered as a carrier and not a spend because 1f authors no beat on it: nothing yet *uses* the difference between what two players hear. | `vfx-proximity` | camp (1f, substrate only — not yet played) |
| Synced audio-visual peak | spike | 4.6 | `unbuilt` | `vfx-audio-sync` | — |
| Sound with no visible source | dread | 2.1, 6.5 | `unbuilt` — **precondition built 2026-08-09 (packet 1f), deliberately not fired.** The ratified darkness law (plan §1.5) inverts this row's economics completely: it was written for a world where a sound with no visible source is a deliberate authored exception, and away from light at night *every* sound now has no visible source by default. That makes the device simultaneously free and much easier to waste — the first creature noise in the dark spends it whether or not anyone meant to. 1f builds the channel and fires nothing into it; the device stays `unbuilt` because the substrate existing is not the same as a beat existing, and whoever authors the first night creature voice should read this row before they do. | `vfx-audio-sync` | — (channel built by 1f; nothing fired into it) |
| Tension-budget pullback (earned breather) | arc | 3.1, 3.4 | `unbuilt` — noted 2026-08-07 (#152): the lit fire is the repo's first candidate for a breather the players **earned by an action** rather than one a budget granted them, which is the more valuable form. Not a spend — `vfx-escalation` still owns the budget and none exists. | `vfx-escalation` | — |
| Non-predictive threshold (entrances that forecast nothing behind them) | dread | 2.1, 8.3 | `unbuilt` — directed 2026-07-28 for the neighbourhood ring; §8.3 applied to *space* rather than to a cue | — (level + house generator) | — |
| Canopy as spatial darkness (depth, not time) | dread | 2.1, 6.1, 6.2 | `unbuilt` → **directed 2026-08-07** (camp woods, Talon's own direction). Darkness keyed to **where you are**, not to when it is or to something approaching — a continuous overhead-cover field whose density withdraws sky, light, and the campfire sightline together as the player pushes deeper. Registered as a device in its own right rather than as a reuse of *Fog as sightline denial*, because that row's own reusability rule is explicit — "fog keyed to a clock is set dressing; fog keyed to the approaching front is the clock made spatial. Reusable elsewhere only where something is actually approaching." **Nothing approaches here**; the player closes the distance, which is the opposite transaction and makes this the *player's* §4.5 choice rather than the world's schedule. It multiplies against the night reversal instead of duplicating it: the same geometry that is merely shaded at noon is the darkest ground on the map after dusk, so one field re-signs the woods twice over. Three constraints are constitutive, not tuning: **(a)** the darkness must have legible *shape* — patchy, with openings that read as ways in — because uniform darkness is §1 anxiety ("diffuse, directionless… not a target — it has no shape"), not dread; **(b)** openings must *thin with depth*, so the recovery window shrinks (§3.4) rather than the dark merely thickening; **(c)** it attenuates, it does not re-tint — see §12's camp-woods entry for why it must not compete with the dusk sweep's warning grade. **(d) added 2026-08-07 by the land pass — the shaft rule.** A beam of light falling through a gap is this device *seen from below*, not a new device, and it inherits the relief clearings' constraint in a sharper form: **a shaft lands on the floor and may be touched; it must never be somewhere a player can stand *in the sky*.** The instant a shaft is enterable it has become an unscheduled relief clearing and the woods have leaked a release nobody placed (§3.2) — and unlike `e4`/`e10` it would leak one *at random*. A shaft is evidence that there is a ceiling and that it is not for you; it is not an opening in it. **(e) added 2026-08-14 by the star-fix pass (NIGHTJOBS-B) — the sky-denial is now PRICED, and pricing it is what protects it.** This row spends its whole budget withdrawing the sky, and until now that withdrawal bought nothing the player could name: you simply could not see up, and a cost nobody can quantify is a cost nobody feels. A celestial bearing readable only at an opening turns the denial into a **currency** — under the canopy you are not merely dark, you are *unable to check*, and the openings this row already requires ("openings that read as ways in") acquire a second function without a single new system. **The exchange runs one way and must keep running one way:** the sky pays for the clearing, the clearing does not pay for the sky. Two refusals keep it that way, and both are constitutive. **A thinning canopy must never become a partial fix** — no graded, half-legible, better-under-sparse-trees sky, because a bearing that degrades smoothly with cover is a continuous compass with extra steps and it deletes this row's (b) (openings thin with depth, so the recovery window shrinks). The sky is readable or it is not, and the edge is the same door the anomaly-pocket row demands. **And no second position-keyed darkness system may be authored to host any of this** — this row is the repo's one darkness field keyed to *where you are*, the anomaly pockets and any always-night interior run at this field's maximum rather than beside it, and a second such system would make two authorities disagree about the same metre of ground. | `vfx-lighting` | camp woods (L-woods-canopy, 2026-08-07, not yet played) |
| Landform as disorientation (the ground decides where you walked) | dread | 2.1, 6.1, 7.2 | `unbuilt` → **directed 2026-08-07** (camp woods, the land pass, Talon's own direction). Every other row in this register is about **what the world does to you**. This one is about **what the world lets you do**: ground with real mid-scale form — draws, spurs, benches, hollows — which off-trail steers a walking player away from the line they intended, so that *the path they actually walked is not the path they believe they walked*. It attacks dead reckoning, which is the instrument left standing after `Canopy as spatial darkness` takes the sightline home. That pairing is the whole point and neither half is worth much alone: withdraw the beacon over a flat floor and the player walks back in a straight line, so the loss is nominal; make straight-line walking impossible while the beacon still shows and the land is merely scenery. **Two constitutive constraints, not tuning. (a) The ambiguity is about POSITION, never about RULES (§7.2)** — the land must be complex, never unpredictable; ground that behaves differently on different visits is broken, not mysterious. **(b) It must ship with a compass it does not invalidate.** Terrain that removes every answer is §1 anxiety again. The one this pass names is *causal drainage*: water runs downhill and collects, so wet, dark, mossy ground means **down** — a rule that is free, works with no light, is honest everywhere, and deliberately does **not** point home. The player is never without information; they are without the information they want, which is §2.1's exact shape. **Amended 2026-08-14 by the night-jobs pass (NIGHTJOBS-B), which put two new wayfinding channels against constraint (b) and reports the result rather than assuming it.** Constraint (b) requires a compass this device does not invalidate, and names drainage as the one it ships with. Two more are now directed, and **the constraint generalises into a rule that binds every future one: a channel may answer a QUESTION, and may never answer WHERE HOME IS.** **(i) Star fixes — compatible, and they strengthen the pairing rather than dilute it.** A fix answers *north*; it never answers *home*, never answers *where am I*, and is unavailable under exactly the canopy this device operates beneath. Drainage says **down**, the sky says **north**, and neither says **home** — two honest compasses that both refuse the one question is §2.1 executed twice, not a leak. **(ii) The radio cord — compatible ONLY as a physical object, and this is the sharp edge.** A line you can pick up and follow *does* lead home, which is prima facie the thing constraint (b) forbids; it survives on exactly the same footing canon already grants the glow-stick trail (*"trails you drop and follow home"*), and the footing is narrow. **The cord is a thing at your feet, not a bearing.** To use it you must already be standing on it, and *finding* it in the dark is the same problem as finding anything else — so it converts "which way is home" into "where is my line", which is a different and honestly-priced question. **Four refusals follow and each one, taken alone, deletes this device:** no cord rendered or audible at range, no through-terrain silhouette or outline, no HUD bearing or distance-to-base readout, and no minimap. This is the standing test for the third and fourth channel too, whatever they turn out to be. | — (level/terrain; not a `vfx-*` problem) | camp woods (2026-08-07, not yet played) |
| Scale that is not the player's (the human band left empty) | creepiness | 1, 6.1 | `unbuilt` → **directed 2026-08-07** (camp woods, the land pass). Talon asked for redwoods — "towering over you… block out the sun… the whole place feels otherworldly" — and *otherworldly* is the operative word, not *tall*. A space reads as **not built for you** when nothing in it is sized for you: elements are either far larger than a person (trunks wider than a player is tall, a ceiling at a height the eye cannot judge) or far smaller (roots, litter, deadfall), and the **1–3 m human band is deliberately left empty**. §1's creepiness is ambiguity about how to read a thing; a room with no furniture at your own scale gives you nothing to read yourself against. The camp woods already half-does this by accident — the canopy pass's light-driven understory inversion clears eye level under closed canopy for navigability reasons — so this row's contribution is to make that **deliberate and protected** rather than a side effect that a later density pass could innocently undo. Generalises well beyond this level: it is the cheapest way to make any space feel like it belongs to something else. The **dials** (trunk taper, bole height, crown ceiling) are not this file's — they belong to `ART-BIBLE.md` / `PROPORTION-STYLE.md` and the builder. This row claims only that the band must stay empty and why. | — (level geometry; dials to ART-BIBLE / PROPORTION-STYLE) | camp woods (2026-08-07, not yet played) |
| **Refuge maintained only by leaving it** | dread | 6.1, 2.3, 3.4, 8.7 | `unbuilt` → **instrument under construction, camp #152 (directed 2026-08-07)**. New row; nothing in the register covered it. §2.3's cost-to-know row trades **safety for information**; this trades **safety for safety** — the refuge's continued existence is purchased only by somebody abandoning it, which is §6.1's prospect/refuge tension made into a verb instead of a geometry. Distinct from *Open sanctuary inverted by dark* (that is the dark re-signing a fixed place; this is the place's existence being conditional on player labour) and from *Forced separation* (see that row: invited, not forced). **#152 builds the instrument only — unlit → lit.** The device is not spent until **#153** (per-time-of-day fire arc, decay through the night, the stoke verb) makes the safety *run out*: a fire that stays lit forever is the §8.7 sanctuary the ledger already fails on three times over, merely with a nicer origin story. Recorded now so #153 is read as this device's payload and not as a fresh idea. Watch two things when it lands: the fuel state must not be mistaken for a second clock (see the §12 camp-campfire entry for why it is not one), and the fire's state must be legible *from inside the woods* or the whole device reads and lands nowhere (§6.0). **The audio half of that constraint is built 2026-08-09 (packet 1f), and the darkness law makes it the *only* half after dark.** "Legible from inside the woods" was written assuming a sightline; §1.5 removes the sightline, so at night the fire's continuous body carrying past the treeline is the entire mechanism by which this device reads at all. Recorded here rather than as a spend because 1f builds the carrier and #153 still owns the payload — but the dependency now runs the other way from how it was written: **if the fire is inaudible from the woods, this device does not merely land softly, it does not exist.** | — (mechanic + Interaction; not a `vfx-*` problem) | camp (#152 instrument; #153 reserved; 1f carries the night-side legibility) |
| Flash beacon (ambiguous position broadcast) | dread | 2.1, 2.3, 5.4, 6.6 | `unbuilt` — directed 2026-08-06 (L5, Issue #108, camp lean MVP). A discrete light event that never differentiates hit/miss/dupe/credited — same trigger condition for all four — so it can never become a §8.3 telegraph for "someone found something." For the shooter it is §2.3's cost-to-know trade (position for information); for everyone else it is §5.4's sensory asymmetry (a bare fact — light, somewhere — with its meaning withheld). Lighter-weight cousin of both rows below, not a spend of either: those stay reserved for an actual continuous vfx-proximity mechanism, this is one pooled one-shot light. Built directly (Interaction + Presentation), not via `vfx-proximity` — scope too small for that skill's continuous-sensing family. Must stay a sharp, brief pulse with no lingering afterglow (a light that stays on stops being an ambiguous event and becomes a findable waypoint — that's the reserved L9 wall-map device, not this one) and a photographically neutral colour temperature (§6.6: warm would read as reassurance, cool would read as alarm — either forecloses the still-reserved fork over whether the flash provokes/deters/reveals a creature). Same light, same SFX, day and night, no invented "night sting" — the charge is entirely inherited from the day/night cycle clock already running (§2.2), never a second clock or a second version of the device. Does not touch or resolve the §6.2 night-floor fork. | — (Interaction + Presentation, built directly) | polaroidlab (dev fixture); camp night pass (L10, not yet built) |
| The unreadable dark (ambiguity about presence, delivered with no event) | creepiness (§1) — and an **investment**, not a spend | 1, 2.1, 6.1 | `unbuilt` — **NOT spent by the main menu after all; see the 2026-08-07 revision in §12.** Directed for it, then released when Talon re-directed the menu to a moonlit reference frame in which the woods are plainly visible. A readable wood is not this device. The device is intact and unspent, and the honest note is that it has now been *proposed and not built* once, which is not the same as being seeded. The device is that **nothing happens**: the dark past the firelight is rendered genuinely unresolvable rather than merely black, and it never once produces an event, a silhouette, a movement or a sound. This is the §1 creepiness definition executed literally — *ambiguity about the presence of threat*, with the ambiguity supplied by rendering instead of by a thing. It is in the same class as §6.3's ambient bed: **it must be deposited before anything can be withdrawn from it.** Establishing "the dark in this game does not resolve" as the world's ambient rule is what gives the first in-game emergence from a treeline a habit to violate (§2.1). Distinct from *fog as sightline denial* (nothing is approaching) and from *night reversal* (there is no day version of this frame to invert). Its whole potency is that it is inert; the moment anything in it moves, it stops being this device and becomes *accumulating wrongnesses* — see the refusal recorded in §12. **Hosted 2026-08-08 by the camp lake (W5) — `deposit, NOT spent`.** Night water is a better host than the menu ever was: it is physically unresolvable rather than merely black, and spec §1.1 makes its inertness a *contract* (no evidence, no objective, nothing to swim for) rather than a director's request — the first host in the repo where emptiness is guaranteed by something other than good intentions. The menu's weather/events carve-out transfers verbatim: swell, glint and shoreline lap are weather; a shape, an uncaused ripple, a wake or a breach are events and are refused outright. The deposit is made or lost in one shader parameter — see the §12 lake entry's specular row. **Third host, and the first non-visual one: the blind night itself (packet 1f, 2026-08-09) — `deposit, NOT spent`.** The darkness law makes the whole night this device's surface, not just a lake or a backdrop, and that promotes the row's weather/events carve-out from a note about one level into **the governing content rule for the night ambient bed**. It transfers verbatim and with no reinterpretation: wind, the low body of the world, the fire's own crackle and the shoreline's lap are **weather** and are allowed; a snap, a footfall, a call, a shift in the undergrowth, anything that reads as a *thing having done something* is an **event** and is refused at the substrate altitude outright. The row's own logic is why — "its whole potency is that it is inert; the moment anything in it moves, it stops being this device and becomes *accumulating wrongnesses*." An eventless bed is not a bed that failed to be interesting; it is the deposit, and every creature packet after this one is drawing against it. **The menu is now permanently unavailable as a host, 2026-08-28 (MENU-1).** The screen was re-directed a second time, to a high-key daylight frame with no dark in it at all. The 2026-08-07 revision *released* this device from the menu; the relight *closes the door* on it. Recorded explicitly so a future director does not propose the menu for it a third time — the two proposals it has already survived are the evidence that it is a tempting host and a bad one. **The menu went dark again on 2026-08-29 (MENU-2) and the door did NOT re-open**: this device needs the dark to be *unresolvable*, and the night frame's dark is completely resolved — a star field fixes the sky's distance, an off-frame campfire fixes the ground's, and the blocks keep lit rims. It is a dark picture, not an unreadable dark. Three refusals now, for three different reasons — too moonlit, too bright, too legible. The three real deposits are unaffected. | — (level/render composition; `vfx-lighting` if it is ever re-staged in a played level; the audio surface is the `sound-*` family) | main menu (proposed, released, then closed 2026-08-28); camp lake (deposit, 2026-08-08, not yet built); the blind night (deposit, 1f, 2026-08-09) |
| **Authority withdrawn (a rule with nobody left to enforce it)** | dread + spike | 2.1, 4.5, 7.2 | `unbuilt` — **new row, added 2026-08-08 by the lake pass (W5).** Nothing in the register covered it. *Night reversal* is geometry inverting its valence as the clock runs; this is **social authority evaporating** — the world's rules are enforced by people, and at night the people are asleep. The same object, the same rule, and nothing behind it. **Blocked on an authority NPC** *(the counselor framing this row assumed is retired — canon §3, 2026-08-22 — and canon names no successor; the block stands, its carrier does not)* (`2026-08-06-camp-map-foundation-design.md` §13; lake contract §1.3, "rope now, counselor later"), and the block is constitutive rather than incidental: §2.1 needs a belief to break, and the belief *"this is a rule"* can only be installed by the day loop actually enforcing it — being turned back, whistled at, marched to the bunk. A rope nobody has ever been stopped at teaches nothing, so crossing it at 2 a.m. is not a transgression, it is swimming. **The lake explicitly may not spend this** — see the §12 lake entry's rope finding, including why loading transgression onto the rope would fuse §8.3's two cue lanes. **First spend needs a new carrier** — the sneak-out-past-the-counselors' -cabin beat this row named is retired with the counselor framing (canon §3, 2026-08-22) and is **no longer canon**; the rope is a fine second. | — (mechanic + level; counselor AI, not a `vfx-*` problem) | — |
| **The blind hide (safety and perception are one resource; the exit is the only instrument)** | dread | 6.1, 2.1, 2.3, 3.2 | `unbuilt` → **instrument under construction, R4 (directed 2026-08-09)**. New row; nothing in the register covered it, and the three near-misses are worth naming because each is one step away. **Not §6.1 false refuge** — that is cover which conceals what is coming *and fails to protect you*; this cover genuinely works, which is what makes it agonising rather than merely unfair, and keeps it honest under §7.2. **Not §2.3 cost-to-know** — that trades safety *for* information; this trades information *for* safety and then charges the reverse trade to get out, so the player is never choosing between two goods, they are choosing when to pay. **Not *refuge maintained only by leaving it*** (the campfire's) — that refuge is a place whose existence is bought by somebody abandoning it; this one is portable, personal, instantaneous, and bought with the player's own senses. The device is that **going down is cheap and coming up is expensive, and only coming up can tell you whether you still needed to be down.** Two constraints are constitutive, not tuning: **(a) the exit must never deliver an all-clear** — standing up may reveal whether the thing is looking at you *now*, and must never reveal whether it has gone, or the release is complete and §3.2 is lost; **(b) something must make the hide finite.** A hide that can be held indefinitely is §8.7's permanently safe sanctuary in the worst form the ledger has yet seen — a *portable* one that travels with the player and never degrades. Hold-fatigue is a physical accident, not a design, and it disappears the moment the packet's own toggle-after-threshold fallback is taken; see the §13 fork this pass adds. | — (mechanic + Interaction; not a `vfx-*` problem) | camp (R4 instrument; the watcher, M2a, is what it is for) |

| **The ration that never refills (a clock you read as a distance, that does not reset)** | dread | 2.2, 3.4, 6.1, 8.7 | `unbuilt` → **driver built 2026-08-09 (packet R5), lighting adapter written and deliberately UNAPPLIED.** New row; nothing in the register covered it, and it is the first entry that answers a standing failure instead of adding a dependency. Every clock in this file so far **resets**: the day/night cycle wraps, the dome front retreats at dawn, the fire can be re-lit. A player can therefore always return to the state they started in, which is why `Permanent safety` (§8.7) fails four times over in §12 and why `Recovery must degrade` (§3.4) has never had a structural pass. This device is a pressure quantity that (a) **only ever decreases, across the whole run**, and (b) **is displayed as a place rather than a number** — the campfire's lit radius, 45 m on night one down to ~12 m on night five, over geography that never changes. The two properties are load-bearing together and worth almost nothing apart: a monotone number is a HUD bar, and a spatial reading that recovers each morning is the dome again. Together they make "how much game is left" something the group reads off the ground in three seconds with no UI, which is also §2.2's *visible, shared, monotonic, inevitable* satisfied more completely than the cycle satisfies it. **Distinct from `Fog as sightline denial`** on that row's own reusability test — it is keyed to something approaching, so it qualifies, but the dome front's contraction is *within* a night and is given back; this one is not. **Distinct from `Night reversal`** (that is the dark re-signing fixed geometry; this is the lit ground itself being rationed). **Three constraints are constitutive, not tuning: (a)** the pool must have a **findable edge** — a light that fades imperceptibly over twenty metres cannot be pointed at, and a device whose whole payload is a reading is worth nothing if the thing to be read has no boundary; **(b)** it must be **deterministic** — this is the level's urgency cue and `LEVEL-BIBLE.md` §8.2 makes an urgency cue a fairness contract, so it may never be made to flicker, vary by player, or hint at a threat (see the §12 entry's §8.3 finding); **(c)** it is **not the fire's fuel** and must never be fused with it — fuel is player-reversible and §12's camp-campfire entry already forbids reading it as a clock, so if both drive the same light the pressure is the ceiling and the fuel modulates underneath. **What it does not do: it does not break.** R5 ships a build with no release in it, by construction — see the §12 dread gate. | — (mechanic + level; `vfx-lighting` when the adapter is applied) | camp night pressure (R5, 2026-08-09; driver in the tree, adapter unapplied, never played) |
| **The watched (attention as the entire weapon)** | dread + creepiness | 1, 2.1, 7.3, 8.1 | `unbuilt` → **instrument under construction, packet R3 (directed 2026-08-09).** New row; nothing in the register covered a threat whose **only act is looking**. Every threat row this file has ever contemplated does something *to* the player; this one does nothing at all, and the whole charge comes from the player knowing they have been picked out. §7.3 makes weakness a design requirement and this is its purest form — there is no counterplay because there is no attack, only the choice of whether to be the most visible thing in the clearing. **It is the first withdrawal drawn against *the unreadable dark*, and it does not spend that row.** That row's own note is what authorises this one: establishing "the dark in this game does not resolve" is *"what gives the first in-game emergence from a treeline a habit to violate."* The deposit is the setup; this is the violation it was made for. **The lake's and the menu's deposits are NOT drawn on and must never be** — no watcher over water, in a reflection, or in the menu backdrop; those two surfaces are contractually inert and the third inoculation finding refuses events on them outright. **Three constraints are constitutive, not tuning. (a) It is never witnessed arriving and never witnessed leaving.** An exit the player watches is an all-clear, and §2.1 forbids the all-clear arriving; a vanish in full view is also the §4.3 "teleported into the sky" class, remembered as a bug. Dwell is *permission* to leave, never a command — it leaves on the first unobserved instant. **(b) It resolves as a BODY and fails to resolve as a SPECIES.** That is the dial, and it is one notch from §8.5: a shape that resolves fully is a categorised mechanic (§8.1), a shape that resolves not at all is noise. **(c) It may never be approached and studied.** Closing the distance must end the sighting, or the player categorises it at leisure and §8.1 collapses on the first curious playtester. | — (entity behaviour + Interaction; not a `vfx-*` problem) | camp night (packet R3, instrument only — not yet placed in a level, not yet played) |
| **Attention as a resource the group can move** | dread → agency | 4.5, 5.3, 5.4, 2.3 | `unbuilt` → **falls out free of packet R3 (directed 2026-08-09), registered separately and deliberately.** The land pass drew this distinction first and it applies exactly: most of this register is about *what the world does to you*; this is about *what the world lets you do about it*. Because the watcher tracks whoever scores highest on visibility, a player who stands up and runs **takes the attention off their friends** — the "doe run" — and a player who drops flat **hands it to somebody else**. No second system authors this; it is the scoring read backwards, which is why it is worth a row: **any threat that targets by a score the players control is this device**, and it generalises past the watcher entirely. It is §2.3's cost-to-know inverted into cost-to-*be*-known, it is the first mechanism in the repo that makes §5.3's togetherness worth anything, and the sacrifice is §4.5 agency in its cheapest possible form. **Constitutive constraint: the transfer must be perceivable, or this device does not exist.** §7.2 requires rules learnable by observation; if a player cannot *see* attention move, the doe run is a rumour passed between players rather than a mechanic, and the panic button will not be trusted (the pivot plan names this as the MVP's only genuinely hard part). The readout is the head turn and the eyes — see the §12 entry, including why the eyes must stay achromatic. | — (entity behaviour; not a `vfx-*` problem) | camp night (packet R3, instrument only — not yet placed in a level, not yet played) |
| **The bounded exception (one learned world-rule broken, in a room with an edge)** | dread + creepiness — and, per Talon 2026-08-13, **wonder**, a register Part I does not yet theorise; recorded verbatim, not derived, and no doctrine section is edited on its strength | 2.1, 6.4, 7.2 | `unbuilt` → **directed 2026-08-13; first spend reserved for the fog savanna.** The anomaly-pocket thesis (`docs/WORLD-ANOMALY-POCKETS.md`, with the 2026-08-13 lean-law addendum) registered as a device so its uses are counted: a bounded region enforces ONE strange landscape law, under-explained *by construction* — no lore, no UI, no marker, no announcement; you find it or you don't. The violation is of the world-model the connective woods teach ("the woods are everything"), which is §2.1's corollary at landscape scale — a history the player cannot read, instead of an intent. Three constraints are constitutive, not tuning: **(a) the edge is sharp, a door not a gradient** — a bounded exception keeps the woods' own learned rules honest (§7.2: darker-still-means-deeper *in the woods*), where a blend would make the whole map's compass lie; **(b) one law per room, nothing else in frame** — a second strange element halves both, and the lean law's floor is Talon's own: *even one tree that's different is enough*; **(c) rationed per run** (the macro pass rolls few) — a world of exceptions has no rules left to break, which is this device's own §8.1 arriving at map scale. Each pocket type decays independently. The first entry is the charge; revisits settle into a wonder-room, which is the intended register and not a failure — the dread half rides the night, the fog, and the walk, not the reveal. **Second spend 2026-08-13 — the postpile** (see §12). Ruled explicitly rather than assumed: a second spend is authorised by this row's own "each pocket type decays independently", so the savanna burned the savanna's law and not the device. What constraint (c) actually binds turns out to be **placement, not existence** — two pocket types may both ship, and may **not be in sight of one another**, because the eye reading one erosional family turns the second into a variation instead of an exception. That is a macro-pass spacing rule and it belongs next to "stranger pockets sit deeper". A **third** pocket type needs a harder argument than the second did; at three, the map is teaching that exceptions are the rule, which is this device's own §8.1 at landscape scale. | — (worldgen + level; each pocket interior gets its own `/direct` pass) | fog savanna (directed 2026-08-13, not built); the postpile (directed 2026-08-13, previz built, not in a level) |
| **The apparent artefact (regularity with no maker, and a real explanation nobody will look for)** | creepiness (§1) + wonder | 1, 2.1, 6.5, 7.2 | `unbuilt` → **directed 2026-08-13 (the postpile).** New row; the anti-inflation check against the four adjacent rows is in the §12 entry and all four miss it. *The bounded exception* is about the **room** — one landscape law with an edge; this is about the **object** — that the law produced something which looks intentional. *Accumulating wrongnesses* compounds and the lean law forbids compounding. *Traces, not exposition* introduces a thing by what it left behind, and every trace implies a maker who **exists**; this implies a maker who **does not**, and that inversion is the entire content. *Scale that is not the player's* is "not built **for** you"; this is "**built** at all." The device is that the world produces one piece of **apparent craftsmanship** — a fitted floor, a fluted wall — in a game whose every other structure is a camp building the players can name. It is §1's creepiness in its textbook form (ambiguity about how to read a thing; disrupted mentalization — is there an agent behind this?) delivered by geology instead of by an entity, which makes it free, silent, and permanent. **Three constraints are constitutive, not tuning. (a) There must be a real, mundane, physically true explanation available and never offered.** The columns fit because they cooled and contracted; a player who looks at the broken talus can see the rock failing that way by itself. This is what keeps the device outside §8.1 — categorising it as **rock** is fine and must stay possible, categorising it as **made** must never be confirmed. A device whose only defence is that nobody investigates is lore in waiting. **(b) Nothing may ever confirm the maker** — no carving, no symbol, no alignment to anything else on the map, no element that was placed rather than formed. One deliberate tile and the lean law is dead and the pocket is a ruin. **(c) It may not be useful.** Apparent craftsmanship that is also good ground becomes a base, and §8.7's permanent safety arrives without anyone designing it — which is exactly the failure the postpile's §12 entry has to record. Generalises well past this pocket: any future cairn, alignment, ruin or geometric formation is drawing on this row, and the second one halves the first. | — (worldgen + level; not a `vfx-*` problem) | the postpile (directed 2026-08-13, previz built, not in a level) |
| **Position without judgment (a channel that answers *where*, and never *whether*)** | dread | 2.1, 5.4, 7.1, 8.7 | `unbuilt` → **constitutive rule established 2026-08-09 by packet 1f (the blind-night substrate).** New row; nothing in the register covered it, and it exists because the ratified darkness law (plan §1.5) creates a hazard no previous direction had to face. Making sound *the* information channel is the only way a near-black night is playable rather than frustrating (§7.1) — but a channel that answers every question defuses the very blindness it was built to survive, and hands §8.7 a **permanently safe sanctuary made of information**: if your ears always tell you whether it is safe over there, losing your eyes cost you nothing and the darkness law is set dressing with extra steps. The device is the line that keeps both halves: **audio may state where a thing is, and may never state what it means.** The fire's body says *a fire, that way, that far*; it may never say *and nothing is standing next to it*. A creature's voice may say *something, roughly there*; it may never say *and it has noticed you*, nor *and it has not*. §2.1 is satisfied in its exact shape — the player is never without information, they are without the information they want — and it is the same shape the land pass already reached from the opposite direction (see *Landform as disorientation*, constraint (b): drainage tells you **down**, and deliberately does not tell you **home**). **Two constraints are constitutive, not tuning. (a) The two cue lanes must be physically separate, not merely conceptually.** §8.3's own parenthetical already draws the line — a fairness/urgency cue stays honest, a dread cue must sometimes lie — and this row adds that in a mix they must not share a bus, a lane or a modulation source, because the instant the fairness lane is modulated by threat state it has become a telegraph and both lanes are lost at once. **(b) Nothing may make the bed react to a threat.** The bed's job is to be the thing a voice is heard *through*; a bed that gets out of the way when something is coming has announced it. This generalises the refusal §12 has now recorded four times against `AmbientBed.Withdraw` (campfire, lake, prone, watcher) and states the positive form of it for the first time. | — (mix discipline; the `sound-*` family builds it, `vfx-audio-sync` rations anything that violates it) | camp night (1f, substrate — not yet played) |
| **Contagious panic (a defence your friends can spend for you)** | release (§3.3 laughter) + dread | 3.3, 5.2, 5.6, 4.5 | `unbuilt` → **directed 2026-08-13 (the electric eel kit, packet STORMVAULT-EEL-2).** New row, and the lake pass's anti-inflation discipline was run before adding it — three rows are adjacent and none covers it. **Not *Forced separation*** (that moves *bodies*; this moves a **resource**, and the two happen to arrive in the same event here only because one mechanism does both). **Not *Attention as a resource the group can move*** (that is the group spending a resource **deliberately** — the doe run is a choice; this is its involuntary inverse). **Not *Per-player sensory asymmetry*** (perception, not property). The device is that **one player's panic empties another player's defence, with no aim, no malice and no way to refuse** — §5.2's emotional contagion built as a mechanic instead of carried by voice, which is the first time anything in this repo has propagated a feeling by any channel other than proximity mic. It is also the first mechanism where a teammate can cost you something *without choosing to*, and that is precisely what keeps it inside the register: the comedy and the fairness argument are the same fact, **nobody could have chosen it**. **Three constraints are constitutive, not tuning. (a) Nobody may ever aim it.** The instant a player can select whose resource is spent, this is griefing in a costume, and §7.2 goes with it — the involuntary bearing and the radial reach are load-bearing for the *affect*, not merely for canon 12's law. **(b) The transfer must be perceivable as ONE event** — you must be able to see that the thing which took your charge is the thing which took theirs. That is *Attention as a resource*'s own constraint arriving from the opposite side, and the mechanism already pays for it (one circle serves blast and chain, MECHANICS §2.5). **(c) The loss costs time and dignity, never progress** — the standing forward constraint the land, watcher, glow-stick and savanna passes each filed, and the one thing that stops §8.9's valve inverting. | — (mechanic + Interaction; not a `vfx-*` problem) | camp (the eel kit, directed 2026-08-13, not built) |
| **Infrastructure as the attack surface (what the world takes is what you built)** | dread | 2.1, 3.4, 4.5, 5.3, 8.7 | `unbuilt` → **directed 2026-08-14 (the radio cord, packet NIGHTJOBS-B).** New row, and the anti-inflation check was run against four adjacent entries before adding it, because this file's own warning is that an inflated register is worse than an absent one. **Not *Refuge maintained only by leaving it*** — that is a place whose *existence* is bought by somebody abandoning it; the thing here already exists and is bought once, and what happens to it is done by the world rather than left undone by the group. **Not *Route loss behind the group*** — that is the level withdrawing terrain the players merely *used*; this is the world destroying an object the players *made*, which is the difference between losing a road and losing your road. **Not *Authority withdrawn*** — that is a social rule with nobody left to enforce it; this is a physical thing with somebody actively unmaking it. **Not *Contagious panic*** — that moves a resource between players; this removes one from all of them at once. The device is that **the group's own construction is the thing the world reaches for**, so every night of investment enlarges the surface that can be taken — which is the only mechanism this file has ever found that makes §3.4's degradation *scale with success* instead of running on a timer. It is also the cleanest available answer to §8.7: safety cannot be permanent when safety is a structure and something eats structures. It is canon 8's *"unless something eats them"* and canon 13's *"banked territory can be fully lost"* read as an affect instrument rather than as an economy rule, and it generalises immediately past the cord to hearth nodes, light lines and rock rings — which is precisely why it is registered now, before three separate passes each invent it privately and burn it three times. **Four constraints are constitutive, not tuning. (a) The loss must be of a thing the players chose to build, in a place they chose to put it** (§4.5) — world-placed infrastructure being destroyed is scenery, not this. **(b) The taking is never witnessed** (§8.1) — you find the aftermath, never the act; a creature filmed cutting your line is a categorised mechanic by night two. **(c) It must cost time and dignity, never the run** (§8.9) — the standing constraint the land, watcher, glow-stick, savanna and eel passes each filed independently, and the one thing that stops the valve inverting into resentment. **(d) At least one cause must be indifferent** (§8.3) — if only the threat unmakes what you built, your ruins are a perfect alarm and the game gets safer the more it is played. | — (mechanic + level; not a `vfx-*` problem) | camp night (the radio cord, directed 2026-08-14, not built) |
| **The unmarked mass (a way through that nobody authored, in a world that authors everything)** | creepiness (§1) → pull | 1, 2.1, 4.5, 6.1 | `unbuilt` → **directed and built 2026-08-29 (TANGLE-1, the tangle, Bubble Test).** New row; the anti-inflation check was run against six adjacent entries and each of them misses. **Not *Scale that is not the player's*** — that row's whole mechanism is the 1–3 m human band left EMPTY so you have nothing to read yourself against; the tangle's band is the fullest part of it (46 rubble blocks at 1.1–4.6 m, every one of them a hold), and the pile is emphatically sized for a climber. It is the same silhouette answering the opposite question, which is exactly the near-miss worth recording rather than absorbing. **Not *Landform as disorientation*** — there the ground overrides where you meant to go; here nothing is taken from you, a route is simply never given. **Not *Stairs/doors that lie about length or destination*** — the tangle's stair does not lie about anything; it is not announced. **Not *Non-predictive threshold*** — that is an entrance that forecasts nothing behind it; this has no entrance. **Not *The apparent artefact*** — that is regularity with no maker; this is irregularity with no maker, and the affect runs the other way (that one says *somebody was here*, this one says *nobody arranged this for you*). **Not *The unreadable dark*** — same family (§1 ambiguity about how to read a thing) and a different instrument entirely: that withholds by opacity, this withholds by clutter, and clutter is readable-in-principle, which is what converts the ambiguity into a pull rather than a hesitation. The device is that **one structure in a legible world has no stated way through it, while its reward is plainly visible from outside** — so the only way to learn the route is to commit to the mass and start reading it with your body. **Three constraints are constitutive, not tuning. (a) The destination must be visible from outside and the route must not be** — a mass with no visible reward is scenery, and a mass with a marked route is a course. **(b) Exactly one route must actually be guaranteed, and it must not be signed** — §7.1's "avoidable but barely" applied to navigation: a pile where every line may cliff out is a tease, and a pile whose good line is painted is a corridor. **(c) The failures must be cheap** (§8.9) — reading it wrong costs the walk back down, never the run; the whole device is a player choosing to spend time on an unreadable thing, and it inverts into resentment the moment being wrong costs anything else. | — (level geometry) | Bubble Test (TANGLE-1) |

**The register is nearly empty and that is the honest state of the game.** TIDE has shipped
one clock and one voice system and has directed neither. Every `unbuilt` row is a dependency,
not a plan — the director may propose one and must flag what it waits on. Nothing here is a
commitment to build.

**Reading the "Built by" column.** It names the `vfx-*` skill that knows *how*, once `/direct`
has decided *whether*. A dash means the device is not a VFX problem — it belongs to a mechanic,
a level, or the voice system, and routing it to a `vfx-*` skill would be the §0 capability
mistake in a new coat. See `docs/ATMOSPHERIC-VFX-INTEGRATION.md`.

**Two devices are blocked on the same class of missing thing, not on effort.** Wrong silence
needs an ambient bed to withdraw (and that bed must fit inside `SfxLab`'s ≤24 concurrent 3D
player budget, which already counts the five voice speakers). The teammate voice cut-out needs
an afterlife channel that does not exist — which also blocks §5.6's failure-as-content. Neither
is a hard problem; both are unstarted, and saying so is more useful than an optimistic state.

## 11. The signature

What is unmistakably TIDE, versus what would be borrowed. **Empty as of 2026-07-25** — a
signature is observed, not declared, and TIDE has not yet produced a moment to observe. The
director adds a line here only when a device has actually landed in a session and someone
described it back.

Do not seed this section with aspirations. An aspirational signature is how a game ends up
imitating its references while believing it has a voice.

## 12. Cooldowns

Per device, per level, per session. Populated by `/direct` write-backs.

> **Ledger reconciled 2026-08-06** (the fork opened 2026-07-28 when PR #76 and the
> neighbourhood-ring direction each kept their own register). Both blocks below are live and
> Part II above now carries both sets of entries. The two levels they were directed against are
> retired as *settings* — the suburban ring by the camp canon, the house as a standalone world —
> but the house interior itself is salvaged forward as the camp's **Sleeping Cabin** interior
> (`docs/superpowers/specs/2026-08-05-lean-mvp-salvage-map.md` §3a), so its device statuses
> travel with the geometry and are not reset by the move. Re-direct via `/direct` when the cabin
> is dressed; do not silently re-spend a device this ledger already marks `spent-here`.

**The neighbourhood ring — directed 2026-07-28 (ordinary houses, entering one).**

| Device | Status in this level | Note |
|---|---|---|
| Non-predictive threshold | `spent-here` | The ring's ordinary houses. Every visible element (garage, lawn, path, façade type) must be statistically independent of every invisible one (entry method, layout, contents). This is §8.3 enforced by construction, and it is the level's whole engine — do not add a second non-predictive system here. |
| Day/night cycle as clock | **borrowed, not re-spent** | Daylight is the standing cost of any detour. No second clock is authored for the exterior (§2.2 / fork §13). |
| Interior larger than its shell | **REFUSED** | See the inoculation finding below. |
| Doorway as proscenium | **untouched** | *(registered on PR #76.)* Routine passage through a door is not the same act as a *framed* view held through one, so ordinary house doors do not burn the reserved EPIC 2 beat. |
| Wrong silence | **untouched** | Still blocked on the ambient bed; nothing here spends or needs it. |
| Night reversal | **not leaned on** | This direction is entirely about the daylight half. `MinAmbientEnergy` untouched; the §6.2 fork is not moved. |

**The inoculation finding.**

The proposal was that a teleported interior need not fit its own footprint: an 8 × 8 m house could
open into something far larger. It is refused, and not because it is a weak idea.

A door that always delivers you to *that house's* real interior **is not lying** — the player is
never deceived about destination, and a load boundary is not a device. But a shell that routinely
contradicts its own interior teaches the player, within about three houses, that **buildings here
do not have honest insides.** Once that is the ambient rule of the world it is no longer a
violation, and §2.1 has nothing left to break.

The damage is not that it *spends* the parked stairs-and-doors-that-lie device *(PR #76, and
`docs/design/stairs-as-portals.md` on that branch)*. It is worse: it **inoculates** against it.
That device's potency rests on most trips being honest — it violates a habit that honest trips
built. Make dishonest interiors ordinary and a staircase running long later reads as a shrug:
buildings do that here. Spending a device buys a moment; defusing one buys nothing.

**The substitution, which delivers what was actually asked for.** Talon's words were *"they don't
know what they just walked into"* — that is about **contents and consequence**, not square footage.
So: the shell tells the truth about its size, and everything else is free. Layout, room count,
sightline depth, what is in it, whether the way you came in is still visible from where you end up.
The whole feeling survives and the parked device stays whole.

**Two gates fail today, and both fail for one reason: nothing bad can happen indoors.**

- **§3.1 break — FAIL.** Entering a house has no possible bad outcome, so the build never breaks.
- **§3.4 degradation / §8.7 permanent safety — FAIL.** Ten more permanently safe rooms.

Time is a genuine cost and makes entering a *decision* (§2.3's cost-to-know trade: daylight for
information). **It is not a stake and does not make entering frightening**, and those are different
claims that must not be collapsed. The cheapest honest stake is §5.4 separation — an interior is
where the group cannot see you and your voice thins — but that is a **dependency, not a direction**:
proximity voice has no occlusion by geometry (§5 scope note) and no threat can be indoors.

**Until one of those lands the ordinary houses are creepy, not dreadful (§1), and worth entering
out of curiosity and supply rather than fear.** That is legitimate to build now — it is the setup
§2.1 needs — but the level must not be called frightening on the strength of it.

**Amended 2026-07-28, same day, after Talon read the above.** He states the target directly:
*"The whole point is the house is an unknown the player should be worried to open the door every
time because they don't know what they'll find."* Two things follow, and they change the status of
this entry rather than its content.

- **The two failing gates are no longer a tolerated condition; they are the stated work.** This
  entry previously recorded §3.1 and §3.4/§8.7 as dependencies to live with. They are now the
  thing the level is *for*, and the honest read is that **the blind threshold is the setup and not
  yet the beat.** An unknown whose worst outcome is *disappointing* stops being frightening after
  about five doors — that is the failure mode to watch for, and it is a §2.1 fuel problem, not an
  atmosphere one. No amount of directing fixes it.
- **Entry state re-rolls per DAY, not per run** (Talon: *"I don't want all houses to be
  interactable in every day"*). This costs nothing and needs no new justification: loop-v1 §8
  already decided that zone interiors re-seed via the dome, so the nightly dome is the in-fiction
  reason tomorrow's doors are not today's. Registered here because it strengthens the
  non-predictive threshold — knowledge of the houses now fails to accumulate even *within* a run.

**Contents grammar resolved, and the resolution is a general rule worth keeping.** Kitchens hold
kitchen things and garages hold tools (§8.5 satisfied). That looks like it breaks the independence
rule, since a garage is visible and would then predict its contents. It does not, because:

> **The grammar governs contents. The independence rule governs consequence.**

A visible feature may honestly advertise *what is in it* — that is learnable world logic and
choosing where to go on it is §4.5 agency. Nothing may advertise **what the threshold does or what
is waiting**. This distinction is the difference between a world that rewards reading it and a
world that leaks its own surprises, and it generalises past this level.

**House interior — directed 2026-07-27 (`--world house`).**

| Device | Status in this level | Note |
|---|---|---|
| Day/night cycle as clock | `spent-here` | The house's only clock. Do not add a second one to this level (§2.2 / fork §13). |
| Open sanctuary inverted by dark | `spent-here` | The great hall's own geometry. Do not re-stage the same inversion elsewhere in this house. |
| Doorway as proscenium | reserved, **unfired** | Cut into the kid's-room geometry (header crops at Y 9.29 m). Held for EPIC 2. Do not fire before tribute exists — see the two failed spike conditions below. |
| Wrong silence | **refused** | No ambient bed exists to withdraw. Spending it now would burn the strongest device in the file on an ambience change nobody would notice. Invest the bed; spend nothing. |
| Night reversal | **leaned on, not resolved** | This direction wants it. It does not overrule `MinAmbientEnergy` (§6.2). Fork stays Talon's. |
| Stairs/doors that lie | **untouched** | Explicitly checked and deliberately not spent. Device remains whole. |

**Two open dependencies recorded against this level, both resolving on EPIC 2:**

1. **§8.7 is violated by the house as it stands.** It is a permanently safe sanctuary. Until
   the tribute makes safety cost something that runs out, this level has no §3.4 degradation
   and its clock has nothing to threaten. This is the level's primary defect and it is a
   mechanic dependency, not an atmosphere one — no amount of lighting fixes it.
2. **The bedroom beat is not yet a spike.** It passes violation, simultaneity, legibility and
   retroactive explicability; it fails **stake** (§4.1.1) and **agency** (§4.5), because
   nothing the group chose causes it. Both resolve the moment tribute lands. Build the beat
   now, leave it unfired.

**The basement has no clock and is therefore atmospheric, not dreadful (§2.2), stated in those
words so the room's quality is not mistaken for the thing it lacks.** Its dread must be
borrowed: descending is a §2.3 cost-to-know trade — time against a clock you can no longer see —
and that trade does not exist until leaving the group costs something.

**Sleeping Cabin — re-directed 2026-08-08 (the same geometry, dressed as a log camp cabin).**
This is the re-direct the reconciliation note at the top of §12 asked for: the interior has been
rethemed (log walls, timber, a stone hearth, antlers, camp dressing) with layout, collision,
sightlines and the lighting arcs unchanged. **Nothing above is reset** — the house-interior
statuses travel with the geometry. The retheme is affect-neutral by construction almost
everywhere: the atrium ceiling was repainted warm timber at *exactly* the luminance of the grey
it replaced (0.394), so the unlit volume overhead that carries half the inversion is unchanged,
and every other load-bearing dark surface holds its old brightness to within 0.01. One change
is **not** neutral and is the reason this entry exists.

| Device | Status in this level | Note |
|---|---|---|
| Open sanctuary inverted by dark | `spent-here`, **unchanged** | The retheme repaints the inversion; it does not stage a second one. No re-spend. The great hall is still the only place in the cabin that does this. |
| Colour temperature as threat channel | **leaned on and refined, NOT re-spent** | The great hall's TV became a stone hearth, and its practical went from cold blue to warm firelight (energy, range and `LightNight` group inherited unchanged, so the night light budget is untouched). This does **not** contradict the level's warm→cold statement, because that statement lives in the **ambient**, which is untouched. `ART-BIBLE.md` §3's C1 reconciliation (decided 2026-07-22) draws this exact line: warm reads on *objects and accents*, cool/dark on *zones*, and "a warm-accent threat standing against a cool danger zone is the intended composition — the accent pops hardest precisely because it contrasts the zone." A warm island inside a collapsing cold volume is that composition. |
| Prospect / false refuge (§6.1) | **improved, still not a device spend** | This is the one gate the retheme genuinely strengthens, and it is why the warm hearth is the better call rather than merely the more plausible one. A cold TV pool *lit* a corner; a fire **invites you to stay in it**, and staying costs night vision and makes you the brightest thing in a 36 × 20 × 10.7 m volume of unlit air. Cover that also exposes is §6.1's definition of false refuge. No new device row: the geometry already carries *Open sanctuary inverted by dark*, and this sharpens that device rather than adding one. |
| Day/night cycle as clock | `spent-here`, **borrowed unchanged** | The hearth rides the existing cycle through `LightNight`. **The fire has no fuel state and must never be given one here without going through §13's *one clock or two* fork** — the camp campfire entry below already records a fuel bar as the single most likely thing to be mistaken for a pressure driver. |
| Refuge maintained only by leaving it | **NOT spent — and a cross-level dependency this entry raises** | The cabin's hearth is a **second, immortal fire**, lit every night, needing nothing, never going out. §10's own row for this device warns that a fire which stays lit forever "is the §8.7 sanctuary the ledger already fails on three times over, merely with a nicer origin story." If Issue #153's per-time-of-day fire arc and stoke verb land for the camp campfire, **this hearth must come under the same arc or the two fires will contradict each other** — one costing labour to keep, one free, in the same run. Recorded now so #153 is not authored against the campfire alone. |
| Wrong silence | **untouched** | Still no ambient bed in *this* level to withdraw. The campfire trunk's `AmbientBed` is a deposit in the camp world, not here (§10). Nothing in the retheme spends or needs it. |
| Night reversal | **leaned on, not resolved** | Unchanged posture. `MinAmbientEnergy` untouched; the §6.2 fork is not moved a millimetre by this pass. |
| Doorway as proscenium | reserved, **still unfired** | Protected, deliberately. The dressing pass wanted cross-joists across the atrium ceiling and **cut them**: the entry→attic-hatch ray enters the joist band a metre before the hatch begins, so joists would have occluded the framed sightline this beat is reserved on. The five beams that shipped clear it at every spacing. |
| Stairs/doors that lie | **untouched** | Unchanged. Still whole. |

**§8.7 fails here exactly as it already failed, and the retheme makes the failure *cosier*.** The
cabin remains a permanently safe sanctuary with no degradation, which the house-interior entry
above already records as this level's primary defect and a mechanic dependency rather than an
atmosphere one. The honest addition is that a hearth is a **more attractive** permanent refuge
than a television was, so the same unfixed defect now pulls harder. That is not a reason to make
the room colder; it is a reason the tribute/decay dependency is more urgent than it looks.

**hoodlab — WP-N1's night devices, built and captured 2026-07-26, never played in a level.**
The dome/darkness instruments (§10's *night reversal* and *day/night cycle as clock* rows) were
headed-captured in the dev lab only. Nothing here is cooling down: a device is spent when a
session spends it, and no session has. Recorded so the next director does not read the §10
`seeded` marks as an exterior level having already been directed — it has not been.

**Camp — directed 2026-08-06 (L10, #113), wiring only, not yet played.** The night pass moves
the dome/atmosphere/fog/colour-temperature instruments off the retired hoodlab world and onto
the camp world, centred on the campfire instead of a private house. This entry exists for the
same reason the hoodlab one does: `dotnet build`/`dotnet test` prove the C# compiles, not that
anyone has stood in it — the agent authoring this had no Godot binary and could not run a
headed capture or a real session. Nothing below is `spent-here`; it is `leaned on`, pending the
first actual playtest.

| Device | Status in this level | Note |
|---|---|---|
| Night reversal | **leaned on, not resolved** | Same posture as house interior (§12 above): this direction wants the reversal and does not touch `MinAmbientEnergy` (§6.2 fork stays exactly where it was — see below). |
| Day/night cycle as clock | **leaned on** | The camp's only clock; this level does not add a second one. Production period is 720s (design §1), not hoodlab's 300s dev-lab period — the front's outer radius was re-derived (160f → 385f, see `CampWorld.DomeOuterRadiusM`'s own doc) to hold the *same* ~1.10x-sprint speed ratio at the longer day, so §7.1's fairness contract carries over unchanged in feel, not merely in code. |
| Fog as sightline denial / Colour temperature as threat channel | **leaned on, unchanged** | Both ride the dome's existing coupling to `CycleDriver.Phase`; nothing in this Story touches either curve. |
| Open sanctuary inverted by dark | **not spent here** | That device is `HouseWorld`'s own great-hall geometry (§12 above), salvaged forward as the Sleeping Cabin interior. The exterior camp world does not attempt its own version. |

**The new fact this level adds, not present in the hoodlab test: the dome now converges on a
*shared, communal* landmark rather than a private one.** hoodlab's front closed on the player's
own house; the camp's closes on the campfire everyone spawned around, sat at, and can see from
every trailhead (`LEVEL-BIBLE.md` §3, §8.2, §8.4 — "the safe direction and the readable
direction agree by construction"). This is worth a note in its own right: §5.1–§5.4 are about
what the *group* shares, and a landmark every player can independently sight, all converging on
the same instant, is a genuinely different reading of the device than one player watching a wall
close on their own front door. Whether it actually lands as *more* dreadful for being shared, or
merely as the same effect seen from more places at once, is an experiential question this Story
cannot answer and does not try to.

**Two gates fail here for the same reason they fail at hoodlab and in the house — carried
forward, not rediscovered.** §3.1 (break) and §8.7 (permanent safety) both fail: no creature or
threat exists in the lean build (explicitly out of scope for L10, per its dispatch), so night
falling has no bad outcome to break into, and the Sleeping Cabin is a sanctuary that never
degrades across the run. Both are the identical dependency the house interior entry already
names (tribute / a threat in the dark), not a new defect this Story introduces.

**A note for the point-of-no-return fork (§13), not a resolution of it.** At the camp's own
geometry, the safe-return radius the escapability contract produces (`CycleBands.SafeReturnRadiusM`
at the production 720s period ≈ 350 m) sits well outside every authored point in the map — the
deepest evidence site is ~140 m from the campfire (`docs/superpowers/specs/2026-08-05-lean-mvp-
and-camp-layout.md` §3). Under the current tuning, nobody can actually be caught by the dome
anywhere in the authored camp, regardless of what being caught would eventually cost. This keeps
the fork exactly as unripe as §13 already records it — if anything, camp's own scale pushes its
ripeness trigger further away, since the geometry itself removes the point-of-no-return this
Story's own front-speed math would otherwise create. Recorded so a future director sizing new
zones (deeper trails, a second camp) knows the 350 m figure is the ceiling before this fork
starts to matter.

**Camp woods — directed 2026-08-07 (the canopy pass), from Talon's own direction.** Altitude:
**level** (the woods as a zone with a gradient), with a *system* pass — the canopy field itself —
flagged for a second look once it has been stood in. Talon's report is the input and it is a
§6.0 affect defect stated in his own terms: he perceives the treeline perfectly well and it feels
like *a small diorama*, because the follow camera floats above it. A cue that is perceived and
feels wrong is this file's defect, not `LEVEL-BIBLE.md` §8's.

| Device | Status in this level | Note |
|---|---|---|
| Canopy as spatial darkness | `spent-here` | The woods' whole engine. Do not author a second position-keyed darkness system anywhere in the camp — a second one makes the first unreadable. |
| Night reversal | **leaned on, not resolved** | Same posture as the house interior and the L10 camp entry above. The canopy is a **multiplier on top of** `DarknessLevel`, never a replacement for it, and level 1 stays the default. §6.2's fork is not moved one inch by this pass. |
| Day/night cycle as clock | **borrowed, not re-spent** | The canopy authors no clock and must not. It is the *spatial* axis; the cycle stays the camp's only *temporal* one (§2.2). Stated because "it gets darker as you go deeper" is easy to misread later as a second clock — it is not; it does not advance on its own and the player can walk it backwards. |
| Fog as sightline denial | **not spent here** | That row is the dome-coupled device and its own note restricts reuse to where something is approaching. Nothing approaches in the woods. If the canopy is *implemented* with fog, that is a mechanism choice for `vfx-lighting` and does not spend this device. |
| Colour temperature as threat channel | **deliberately untouched — hard constraint** | §10's own note says re-tuning this to buy something else would spend a landed effect. So: **the canopy attenuates; it does not re-tint toward a new threat colour.** Whatever hue it carries must be the one colour that reads as physics rather than warning — light that has passed through leaves — and it must sit *under* the dusk sweep's orange→plum→indigo grade, never across it. A canopy that introduces its own warning colour puts two warning channels in the same lane and neither survives. |
| Open sanctuary inverted by dark | **not spent here** | That device is enclosed-and-safe becoming enclosed-and-hostile. The canopy is the inverse shape — never desirable, only more so. Different device; that one stays whole. |
| Accumulating wrongnesses | **untouched, and hosted** | Not spent. Recorded because the canopy field is the natural *driver* for it later: the deepest cover is where a wrongness is least explicable. A future director gets that for free and should take it. |
| Non-predictive threshold | **checked, not violated** | A canopy opening honestly leads into denser woods. That is `LEVEL-BIBLE.md` §8.2's fairness contract (a warning that a state is changing stays honest), not §8.3's dread telegraph. §8.3 itself names these as different cues that must not share a channel — they do not here. |

**The break is the moment of stepping under.** Not a timed instant — at level altitude the break
is positional. Prospect (§6.1) is withdrawn **upward**, which is the one direction a player never
guards, and the gradient's real content is the progressive loss of the campfire beacon that
`LEVEL-BIBLE.md` §8.4 makes the camp's whole wayfinding contract ("toward home is legible by
sightline alone"). That is what earns the beat its position and it is why it cannot move: the
trailhead openings are the only place the transition is legible, and they are the last place
there is sky.

**Two directions that are requirements, not tuning, and are easy to lose in implementation.**

1. **A relief clearing must never restore the beacon.** If the campfire is visible from `e4` or
   `e10`, the release is *complete*, the player returns to zero, and the next build starts over
   (§3.2). Light comes back; home does not. That is the incomplete release, and it is the whole
   reason those two clearings are worth keeping.
2. **The attenuation must be bounded, and the bound is not §6.2's.** `DarknessLevel` is Talon's
   call about the *night floor*. Canopy cover is a multiplier stacked on top of it, and an
   unbounded multiplier can drive any of the three levels below navigability without anyone
   having decided to. So: **darkest canopy × deepest night × level 1 must still be walkable out
   of.** That is §7.1's fairness contract applied to a new axis the §6.2 dial does not cover, and
   asserting it does not resolve the fork — it protects it, by keeping the canopy from
   pre-empting Talon's choice.

**Dread gate.** *Ambiguity* — **pass, conditionally**: the canopy denies the sightline home, so
"where am I" stops being answerable by looking. The condition is constraint (a) on the device row
— uniform darkness resolves nothing *and* reveals nothing, which is §1 anxiety, not dread. *Clock*
— **pass by inheritance only**; borrowed from the cycle, and the woods author none. *Break* —
**FAIL**, carried forward unchanged from the L10 camp entry: no threat exists in the lean build,
so pushing deeper has no bad outcome and the build never breaks. *Incompleteness* — **pass**, via
the relief clearings under requirement 1. *Degradation* — **pass structurally, unbuilt in
substance**: openings thinning with depth shrinks the recovery window for free (§3.4), which is
the cheapest degradation in the file, but the *sanctuary* problem below is untouched. *Affect* —
**pass, conditionally**: enclosure is felt rather than read, and the condition is that the crown
sits above the **camera**, not above the avatar. A canopy the follow camera floats over is the
defect Talon reported, restated at a larger scale. *Fairness* — **pass**, with requirement 2 as
its floor; darker-means-deeper is the most learnable rule available and it is honest in both
directions.

**Spike gate: no spike claimed.** A gradient is not a beat. Nothing here is meant to be clipped,
and directing it as though it were would be exactly §4.4's inflation.

**Prohibitions, all nine.** §8.1 over-exposure — n/a, nothing is shown. §8.2 startle as build —
**a live constraint**: no darkness *snap* at a canopy edge; the transition is continuous or it is
a startle doing a build's job. §8.3 reliable telegraphs — not violated today, and **flagged for
future work**: darkness must correlate with *depth*, which is geography. The first packet that
spawns a creature preferentially in the darkest cover turns the canopy into a reliable telegraph
and flips this row to violated — that is a constraint on creature placement, recorded here
because the packet that breaks it will not be this one. §8.4 scripted set-pieces — not violated;
a field *is* the possibility space §8.4 asks for. §8.5 illegible chaos — **the live risk**:
scattered trees with no readable structure are noise, so the field must be authored (openings,
corridors, dense hearts) rather than left as pure noise. §8.6 unrelenting tension — the relief
clearings are the answer, which is why they are a requirement and not a nicety. §8.7 permanent
safety — **FAIL, carried forward**: the camp core stays permanently bright and the Sleeping Cabin
never degrades. The canopy makes that contrast *sharper* without fixing it, and sharpening a
contrast is not the same as paying for it. §8.8 streamer-first — n/a. §8.9 punitive failure —
n/a; nothing can fail yet.

**The honest summary: this pass builds the setup §2.1 needs, and only that.** It makes the woods
a place with an inside, which the camp did not have. It does not make them dangerous, because
nothing in the lean build can be dangerous. Say so rather than calling the level frightening on
the strength of its atmosphere — that is the §1/§2.2 distinction ("a level with no clock is not
dreadful, it is atmospheric") applied to a level that has a clock but nothing for the clock to
threaten.

**No new forks.** The canopy raises no decision Talon has to make: the ratio question (§9) is
untouched because no absurd payoff is invented here, and §6.2 is protected rather than pushed.

**Camp woods, THE LAND — directed 2026-08-07 (the terrain-and-forest pass), from Talon's own
direction.** Altitude: **level** — the woods' interior geography, which did not exist before this
pass (the zone had one axis: in and out). A *system* pass on the forest-floor register is nested
under it and flagged for a second look once someone has walked it. This entry is the sibling of
the canopy entry above and the two are one direction in two halves: **the canopy took the
sightline home; the land takes dead reckoning.** Neither is worth much without the other, and
that dependency is stated in the new §10 row rather than left to be rediscovered.

| Device | Status in this level | Note |
|---|---|---|
| Landform as disorientation | `spent-here` | The land's whole engine. Do not author a second disorientation system in the camp — a map that cheats you two ways teaches players it cheats, and §7.2 goes with it. |
| Scale that is not the player's | `spent-here` | The 1–3 m band under closed canopy is now **protected**, not incidental. A later density or dressing pass that fills eye level with human-scale props undoes this device *and* the navigability floor in the same stroke. |
| Canopy as spatial darkness | **composed with, not re-spent** | Every darkness, damp and thinning value in this pass reads the **existing** canopy field. No second position-keyed darkness system was authored — that was the canopy entry's explicit prohibition and it is honoured. The shaft rule added to §10's row (d) is a *constraint on that device*, not a new one. |
| Fog as sightline denial | **not spent here, unchanged** | The canopy entry already ruled that implementing the canopy with fog is a mechanism choice, not a spend of the dome-coupled device. Haze in the woods is the same case. Nothing approaches; nothing is spent. |
| Colour temperature as threat channel | **deliberately untouched — and this pass is where it was most at risk** | A damp shaded hollow *wants* to go cool and blue-green, and cool is the threat direction (§6.6). **Requirement: a hollow reads wetter and darker by losing VALUE and CONTRAST, not by gaining blue.** The only hue this pass may introduce is the one that reads as physics rather than warning — light that has passed through leaves. The dusk sweep's orange→plum→indigo grade stays the sole warning lane. |
| Night reversal | **leaned on, not resolved** | Fourth entry in a row with this posture, and it stays. Terrain contributes nothing to `DarknessLevel` and must not. §6.2 is not moved. |
| Day/night cycle as clock | **borrowed, not re-spent** | The land authors no clock. Stated because "mist gathers in the hollows at dusk" is an obvious and tempting idea that would be a *second temporal axis* if it ran on its own curve; if it is built at all it rides the existing cycle. |
| Accumulating wrongnesses | **untouched, and now hosted twice** | The canopy entry recorded the field as its natural *driver* (deepest cover = least explicable). The land adds the other half: a **place**. A wrongness needs somewhere to be, somewhere returnable to, and somewhere hard to leave — a hollow is all three. Still not spent; a future director gets both halves free. |
| Route loss behind the group | **newly hostable, NOT claimed** | Terrain that steers is *not* route loss: the player's own choices compound, the world takes nothing. But a draw that is easy to descend and costly to re-ascend is the first honest, unscripted host this `unbuilt` device has ever had in this repo. Recorded so the packet that implements it knows the ground is already there. |
| Non-predictive threshold | **checked, not violated** | A hollow honestly leads down and a spur honestly leads up. That is learnable world logic (§7.2), not a §8.3 telegraph. |
| Open sanctuary inverted by dark | **not spent here** | Still `HouseWorld`'s great hall, salvaged to the Sleeping Cabin. The woods attempt no version of it. |

**Three directions that are requirements, not tuning.**

1. **The ground must be readable, and must not tell you where home is.** Causal drainage is the
   compass: wet, dark, mossy ground means *down*. It costs nothing, survives the dark, and points
   at the lake rather than at the campfire — so it satisfies §7.1's fairness floor without
   restoring the beacon §12's canopy entry spent a whole pass withdrawing. If damp ever appears on
   high ground for a texture reason, the compass has lied and §7.2 is broken, not decorated.
2. **Every height change must be legible from a walking approach.** A drop the player cannot see
   coming is a startle doing a build's job (§8.2) *and* a fairness break (§7.1) in one move. The
   land may cost effort and direction; it may never cost footing.
3. **The relief clearings must sit on ground a player can stand still in.** `e4` and `e10` are
   §3.2's incomplete release and a release you have to brace against is not one. Flat or benched,
   not on a slope.

**Dread gate.** *Ambiguity* — **pass**, and this pass *adds* fuel rather than spending it: "where
am I" was already unanswerable by sightline, and is now unanswerable by memory of one's own path.
The wet-means-down rule returns a partial answer, which is §2.1's exact shape — an answer that
does not resolve the question. Conditional on the device row's constraint (a): complex, never
unpredictable. *Clock* — **pass by inheritance only.** The land authors none. Stated in the
section's own words: **a land with shape is not dreadful, it is a place.** *Break* — **FAIL**,
carried forward unchanged for the third consecutive entry: nothing in the lean build can be
dangerous, so being lost costs nothing. **One thing this pass does change about that failure,
recorded and not claimed:** §12's L10 note observes the camp is small enough that the dome's
safe-return radius (~350 m) exceeds every authored point (~140 m), so nobody can be caught. A land
where the return is *longer than the straight line* is the first mechanism in the game that erodes
that margin without needing an entity. It is not a break today. It is the cheapest honest candidate
for one. *Incompleteness* — **pass**, inherited, with requirement 3 as its new floor. *Degradation*
— **pass structurally, unbuilt in substance**: the return costs more in effort the deeper you went,
which is §3.4 for free, exactly as the canopy's thinning openings were. *Affect* — **pass,
conditionally**, and the condition is the one that decides whether any of this worked: **the land
must be felt through movement, not seen from a viewpoint.** If it only reads in an elevated
overview capture, that is a §6.0 defect of precisely the kind Talon reported about the treeline —
perceived, and feeling like nothing. The test is not "does the capture look like terrain"; it is
"does a hundred metres off-trail feel different from a hundred metres on it." *Fairness* —
**pass, with two floors**: requirement 2 (no snag, no invisible drop, every pushed-onto slope
walkable off) and requirement 1 (the compass is honest everywhere it appears, because it is the
only one left).

**Spike gate: no spike claimed.** A landscape is not a beat. Directing one as though it were is
§4.4 inflation, and the woods already carry a gradient that is correctly not a spike.

**Prohibitions, all nine.** §8.1 over-exposure — n/a, nothing is shown. §8.2 startle as build —
**live constraint**, requirement 2. §8.3 reliable telegraphs — not violated (drainage is a
§8.2-lane *fairness* cue, honestly reported, not a dread hint), with **one new flag matching the
canopy's**: hollows are now the most obvious place to put a creature or a wrongness, and the first
packet that does so *reliably* converts the terrain into a telegraph. Recorded now because the
packet that breaks it will not be this one. §8.4 scripted set-pieces — not violated; a landform is
a possibility space. §8.5 illegible chaos — **the live risk, and it is the same one the canopy
had.** Terrain that is merely noisy is noise. The land must have **causal structure** — water
carved it, so it drains somewhere, and the somewhere is the lake. A player who notices has learned
the map; one who does not still walks a coherent space. Noise-as-terrain fails this and is the
single most likely way to get this pass wrong. §8.6 unrelenting tension — n/a; the camp core and
the relief clearings still hold, and requirement 3 protects the latter. §8.7 permanent safety —
**FAIL, carried forward.** The core stays bright and flat and the cabin never degrades. The land
*sharpens* that contrast — the core becomes the only easy ground as well as the only bright ground
— without paying for it, and sharpening a contrast is not the same as paying for one. §8.8
streamer-first — n/a. §8.9 punitive failure — n/a, nothing can fail; **forward constraint** for
whoever makes being lost cost something: it must cost **time**, never progress.

**One new fork**, added to §13 and deliberately not surfaced to Talon, because it is unripe and
nobody has walked this ground.

**Main menu — directed 2026-08-07 (the campfire frame, replacing the 2D `TitleScreen` + `MainMenu`).**

A menu is a peculiar thing to direct and the peculiarity is the finding: **it has no clock and
must never be given one.** A menu with a countdown is hostile UI, not dread. Per §2.2 that
settles the target — this frame cannot be dreadful and no amount of atmosphere will make it so.
**It targets creepiness (§1)**, which is the correct register anyway: cheap, needs no entity,
and defined precisely as ambiguity about the *presence* of threat.

| Device | Status in this level | Note |
|---|---|---|
| The unreadable dark | `spent-here` (on ship) | Its first use. The frame's entire dread apparatus. Do not add a second ambiguity system to this scene. |
| Accumulating wrongnesses | **REFUSED** | See the second inoculation finding below. |
| Colour temperature as threat channel | **borrowed, not re-spent** | The warm firelit pocket against cold black is §6.6's vocabulary at rest. The *beat* is "a warm place gone cold" and it does not happen here — the fire never dies, never cools, never gutters out. Deliberately not spent on a menu. |
| Night reversal | **not touched** | There is no day version of this frame to invert. `MinAmbientEnergy` untouched; the §6.2 fork is not moved, not leaned on, and not weakened by anything here. |
| Wrong silence | **untouched** | Still blocked on the ambient bed. Flagged as an opportunity, not spent — see the handoff below. |
| Doorway as proscenium | **untouched** | No framed view is held through anything. The reserved EPIC 2 beat is whole. |

**The second inoculation finding, and it is the reason this direction is worth writing down.**

The obvious idea for this scene is that something should occasionally happen out in the
treeline — a shape, a shift, a branch. It is refused, for the same structural reason §12's
oversized-interiors proposal was refused, and the reason is worth stating in general form:

> **A surface the player sees dozens of times, that reliably produces a wrongness, teaches that
> wrongnesses are decoration.**

The main menu is the single most-repeated frame in the game. A player will sit in it before
every session for the life of the build. Put an event in it and within a handful of launches the
ambient rule of the world becomes *things move in the dark here and nothing ever comes of it* —
at which point the first in-game emergence from a treeline is a shrug, and §10's *accumulating
wrongnesses* row has been defused before it was ever spent. Spending a device buys a moment;
defusing one buys nothing. This is the §12 inoculation logic applied to repetition instead of to
frequency, and the menu is the highest-repetition surface that exists.

**The substitution delivers the same feeling and costs the register nothing.** The dark is not
empty and it is not populated — it is *unresolvable*, and it stays that way for as long as anyone
looks. Nothing needs to move, because the player cannot establish that nothing is there. That is
§1's creepiness definition met exactly, and it is a **deposit**: it installs the habit that a
later violation gets to break.

**§8.8 is the standing risk for this scene and it is named here so the next director checks it.**
A main menu is the most screenshot-first surface a game has, and the refused treeline-event was
partly a store-page idea wearing a design argument. The test for any future addition here: *is
this for the person sitting alone at 1am about to press start, or is it for the trailer?*

**Two prohibitions are enforced as hard refusals, not preferences.** §8.1 — nothing is ever shown
in the treeline: no creature, no silhouette, no eyes, not once, not rarely, not as an easter egg.
§8.2 — there is no menu startle: no sudden sound, no sting, no cut to anything. A menu jump scare
is the cheapest object in horror and this frame will not carry one.

**One fairness item, and unlike §6.2 it is not forked.** The scene is near-black by design and
blurred at rest by design, which means the player must still always be able to find, read and
operate their five options — including a player who never touches the mouse. For a *menu*,
navigability wins outright; there is no legitimate reading in which being unable to start the
game is atmosphere. Keyboard and pad traversal must rack focus exactly as hover does.

> **⚠ REVISED SAME DAY, 2026-08-07, by Talon — most of the entry above is superseded.** He
> supplied a reference frame (a moonlit pine wood, a fire burning off to one side, hanging
> lights bokeh'd through the trees) and re-directed the screen to it. Three things changed and
> the entry is left standing rather than deleted because the *reasoning* in it is still the
> reasoning that applies next time:
>
> - **The menu is now NON-DIEGETIC.** The wordmark and options are a 2D layer over a live 3D
>   backdrop; they are not objects in the woods. Everything above about physical markers, a
>   camp gate and a focus rack between them describes a design that was built and then replaced.
> - **The unreadable dark is NOT spent here.** That device needs a dark that does not resolve,
>   and this frame is deliberately moonlit — the woods are plainly there. The device returns to
>   the register unspent. This is a better outcome than it looks: it was never a good idea to
>   spend the game's cheapest ambiguity device on a screen with no stakes, and the reference
>   settled that by accident.
> - **What the frame now produces is atmosphere, not creepiness.** It is a beautiful place with
>   a fire in it. That is a legitimate thing for a main menu to be and it should be called what
>   it is — the earlier claim of creepiness rested entirely on the dark being unreadable, and
>   with that gone the honest register is "inviting, slightly lonely", not "unsettling".
>
> **What survives unchanged:** the inert-backdrop rule. Nothing in those woods may ever produce
> an event — no shape, no movement, no silhouette, no sound. The reasoning below about
> repetition and inoculation does not depend on the dark being unreadable and applies to a
> moonlit frame exactly as much. The fire, the embers and the hanging lights are the permitted
> motion; they are weather, not events.

**What fails here, stated plainly so the frame is not mistaken for more than it is.** §2.2 (no
clock) and §3.1 (no build, therefore no break) both fail, by construction and correctly. This
frame is **atmospheric, not dreadful** (and, after the revision above, not creepy either), it is
a baseline rather than an arc, and it should never be cited as evidence that TIDE is
frightening. It is the setup §2.1 wants and nothing more.

**Main menu, SECOND RE-DIRECTION — 2026-08-28 (MENU-1, the light frame). `scene` altitude.**
**The campfire frame directed above is gone.** Talon supplied four previz frames and approved one:
a high-key daylight world, blue with distance, heavily blurred colour blocks, one soft soap bubble
right of centre, a wordmark on a luminous scrim with an iridescent underline, and one ember button.
The 2026-08-07 entry and its same-day revision are left standing because their *reasoning* is what
governs this frame too — in particular the second inoculation finding, which transfers whole and
is the single most load-bearing sentence for whoever touches this screen next.

**The register changed and the honest name for it changed with it.** The campfire frame was
"inviting, slightly lonely." This one is **inviting, weightless and unplaceable**: a bright,
hazy, out-of-focus somewhere with no people, no camp, no fire and nothing in the human band. It
still has no clock, still cannot break anything, and still targets neither dread nor creepiness.

| Device | Status in this frame | Note |
|---|---|---|
| The unreadable dark | **still NOT spent — and the menu is now permanently unavailable as its host** | The 2026-08-07 revision released it when the frame became moonlit. The relight closes the door: there is no dark here at all. The row's three real hosts (camp lake, the blind night) are unaffected. Recorded so nobody proposes the menu for it a third time. |
| Night reversal | **day side deposited — NOT spent** | The 2026-08-07 entry said "there is no day version of this frame to invert." **There is now,** and it is the most-repeated surface the game has: a player meets this bright, safe, sunlit reading of the world before every session for the life of the build. §6.2's whole mechanism is the same space re-signing itself; the frame that installs the day reading, hundreds of times, at no cost, is a deposit against it. Nothing is withdrawn here and the §6.2 fork is not moved, not leaned on and not weakened. |
| Colour temperature as threat channel | **borrowed at rest, not re-spent** | §6.6's vocabulary is present and inverted from the campfire frame's: cool now reads as *distance* rather than as threat (the blue depth), and the one warm object on screen is the button the player is meant to press. That is the vocabulary used as grammar, not as a beat. |
| Accumulating wrongnesses | **REFUSED, same refusal, new surface** | See below. |
| Wrong silence, Doorway as proscenium, everything else | **untouched** | Nothing here reaches them. |

**The second inoculation finding transfers verbatim, and the new frame gives it a new tempting
form.** The 2026-08-07 refusal was "something moves in the treeline." The 2026-08-28 version is
**"the bubble pops."** It is the same mistake wearing better clothes, and it will come back —
probably as a transition on PLAY, probably framed as delight rather than as dread. It is refused
for the identical structural reason:

> **A surface the player sees dozens of times, that reliably produces an event, teaches that
> events on that surface mean nothing.**

**So the inert-backdrop rule survives the relight unchanged, and here is the carve-out in this
frame's own terms.** Permitted, because it is *weather*: the film drifting through its colours,
the camera's few centimetres of sway, the underline's travelling highlight, the grain. Refused,
because it is an *event*: a bubble popping, a bubble arriving, a bubble leaving, a bubble
reacting to the cursor or to a press, anything appearing in the blur, anything in the blur
resolving. The test is the same one the woods got — *did a thing do something?* — and it does
not soften because the thing is pretty.

**§8.8 is again the standing risk and it is sharper here than it was.** A bright bubble frame is
a store-page asset in a way a near-black wood was not, and the honest reading is that this one
*is* partly for the trailer. That is allowed — a product's face may be attractive — but it
creates a real cost the register should carry: **the menu no longer tells the player what game
this is.** The campfire frame said "horror" before anyone pressed anything; this one says
"summer." Per canon direction fact 1 that is defensible and possibly better — the double game is
a real camp co-op by day and the dark at night, and a sincere, beautiful day face is the *belief*
the night exists to break (§2.1's setup, exactly). But it only works if the night is carried
somewhere the player meets it before they play. **Forward constraint:** the menu is now the
setup, not the sample. Nothing may be added to this frame to "hint at the horror" — a menu that
winks is worse than one that does not speak — so the night has to arrive in the trailer, the
store page, or the first dusk, and if it arrives in none of those the violation has nobody to
land on.

**§8.1 and §8.2 are enforced here as hard refusals, same as before.** Nothing is ever shown in
the blur — no shape, no silhouette, no figure, not once, not rarely, not as an easter egg. There
is no menu startle: no sting, no sudden sound, no cut. Both survive the relight untouched.

**The fairness item survives the relight and was the packet's actual driver.** The frame is
blurred at rest by design and its type sits on a rendered image, which means the player must
still be able to find, read and operate every option — including a player who never touches the
mouse. For a menu, navigability wins outright; there is no reading in which being unable to start
the game is atmosphere. MENU-1 is the first pass on this screen where that is measured rather
than asserted, and it is also where the previous button failed: it wore its accent as a permanent
ring at rest, so the resting state already looked focused and controller focus had no cursor left
to be.

**What fails here, stated plainly.** §2.2 (no clock) and §3.1 (no build, therefore no break) both
fail by construction and correctly, exactly as they did for the campfire frame. This frame is
**atmosphere and product identity**, it is a baseline rather than an arc, and it must never be
cited as evidence of what TIDE feels like to play.

**Main menu, THIRD RE-DIRECTION — 2026-08-29 (MENU-2, the night frame). `scene` altitude.**
**The light frame directed above lasted one day.** Talon reversed it the same afternoon he
approved it ("clinical and sterile"; target "dark, moody, campfire-at-night"), and on 2026-08-29
removed the ember button by name: *"I don't like the orange bar that is the button for the host
game simply please remove this orange it clashes with the game aesthetic overall."* The frame is
now a night world — near-black blue sky, a star field, blocks fallen to silhouette with lit rims,
an off-frame campfire low-left, and one emissive bubble. The UI accent went cool (moonlight) and
the only warm light left on the screen is the fire, which is outside the frame.

**The register changed for the third time.** Campfire frame: "inviting, slightly lonely." Light
frame: "inviting, weightless and unplaceable." This one is **calm, cold, and warm somewhere you
cannot see** — the one comfortable thing in the picture is off the edge of it, and the player is
outside its reach looking at what it lights. That is a better sentence about this game than
either predecessor managed, and it is still not dread: no clock, no build, nothing at stake.

| Device | Status in this frame | Note |
|---|---|---|
| The unreadable dark | **still NOT spent, and the door MENU-1 closed stays closed** | The obvious reading of this pass — "the menu went dark again, so the host is available again" — is **wrong, and this row exists to stop it.** The device requires the dark to be genuinely *unresolvable*. This frame's dark is completely resolved: stars fix the sky's distance, the campfire fixes the ground's, the blocks keep readable rims, and nothing in it is ambiguous about anything. It is a dark *picture*, not an unreadable dark. The menu has now been proposed for this device twice and refused three times, for three different reasons — too moonlit, too bright, too legible — which is the strongest evidence in the ledger that it is a tempting host and a bad one. The three real deposits (camp lake, the blind night) are unaffected. |
| Night reversal | **the day-side deposit MENU-1 made is WITHDRAWN — and this is the one real loss in this entry** | MENU-1's deposit was precisely *"a player meets a bright, safe, sunlit reading of this world before every session for the life of the build."* That surface now shows the night reading instead, so the deposit is not spent — it is **gone**, and §6.2's mechanism has lost the cheapest day valence the repo had. Recorded as a loss rather than argued about: Talon's direction outranks a ledger deposit, and a director does not lobby to keep a frame he has re-signed. **Consequence, and it is the whole reason this is written down:** the §13 fork *"where the night is carried"* has inverted into *"where the day is carried"*, and the answer can no longer be "the title screen, hundreds of times, for free." Nothing here moves the §6.2 fork itself, and `NightAmbientFloor` was not touched. |
| Colour temperature as threat channel | **still borrowed as grammar, not re-spent — and the polarity has now inverted twice in two days** | Campfire frame: warm meant safety and the cold woods were the threat. Light frame: cool read as distance and the one warm object was the button. This frame: warm is **firelight in the world, off-frame and unreachable**, and the interface is cool. That third arrangement is the one that matches canon (fire is safety, and it is a thing in the world rather than a thing on the interface) — but the fact that it has flipped twice inside forty-eight hours is itself the finding. **A vocabulary that reverses this easily is grammar, and grammar is not a device spend.** Nobody may cite the menu as evidence of what warm or cool means in this game. |
| Accumulating wrongnesses | **REFUSED, same refusal, third surface** | See below. |
| Wrong silence, Doorway as proscenium, everything else | **untouched** | Nothing here reaches them. |

**The inoculation finding transfers verbatim for the third time, and the night frame has supplied
a new and much more tempting form of it: the campfire flickers.** The 2026-08-07 refusal was
"something moves in the treeline"; the 2026-08-28 version was "the bubble pops"; the 2026-08-29
version is **a flicker, a breath, or a crackle on the off-frame fire.** It will be proposed, it
will be framed as life rather than as an event, and it is refused for the identical structural
reason:

> **A surface the player sees dozens of times, that reliably produces an event, teaches that
> events on that surface mean nothing.**

**The carve-out, restated in this frame's terms so the line is unambiguous where the new elements
are concerned.** Permitted, because it is *weather*: the film drifting through its colours, the
camera's few centimetres of sway, the underline's travelling highlight, the grain. **The fire's
glow is weather ONLY while it is constant** — it is a static gradient today and it must stay one.
**The stars are weather only while they are fixed**; a twinkle is a per-star event on a surface
seen hundreds of times and is refused on the same grounds, with the additional note that it is
the single cheapest thing to add to the shader that already draws them. Refused, because they are
*events*: a bubble popping, arriving, leaving or reacting; a flicker, a surge or a pulse in the
fire; a star twinkling, appearing or falling; anything appearing in the blur, anything in the
blur resolving. The test is the one the woods got — *did a thing do something?* — and it does not
soften because the thing is small.

**§8.8 is sharper again, and in the opposite direction from MENU-1's reading.** MENU-1 recorded
that a bright bubble frame is "a store-page asset in a way a near-black wood was not." A *dark*
bubble frame is more of one, not less: it is the most obviously screenshot-shaped version of this
screen the project has produced. The cost MENU-1 named has not gone away, it has changed sides —
the menu now tells the player this is a night game and stops telling them it is a summer camp,
and the sincere day face that §2.1 wants as the belief the night breaks has nowhere else cheap to
live. **Forward constraint, unchanged in force and reversed in content:** nothing may be added to
this frame to hint at the day either. A menu that winks is worse than one that does not speak,
and that was never an argument about which direction it winked in.

**§8.1 and §8.2 are enforced here as hard refusals, same as both predecessors.** Nothing is ever
shown in the blur — no shape, no silhouette, no figure, not once, not rarely, not as an easter
egg. **The night frame makes this materially more tempting and the refusal materially more
important:** a dark, blurred field is exactly the surface on which a half-seen shape reads as
free atmosphere, and it is the same §8.1 collapse whether the field is bright or dark. There is
no menu startle: no sting, no sudden sound, no cut.

**The fairness item is where this pass did most of its actual work, and it is measured.** Talon's
instruction removed the screen's accent colour, and an accent is not decoration on a
controller-first screen — it is how the player finds the one thing to press. Every text and
control pair was re-measured off the rendered frames rather than asserted from source, and every
one came out **better** than the light frame it replaces: the primary action's label went from
5.39:1 to 7.70:1, the wordmark from 5.40:1 to 10.97:1, the secondary row from 4.90:1 to 7.73:1,
the build stamp from 6.09:1 to 6.67:1, and both focus-ring adjacencies from 7.90/5.57 to
8.30/8.31. **Removing the colour Talon objected to cost the screen nothing in legibility**, which
is the answer to the only question this refusal could reasonably have raised.

**What fails here, stated plainly, and it is the same list twice over.** §2.2 (no clock) and §3.1
(no build, therefore no break) fail by construction and correctly, exactly as they did for both
predecessors. This frame is **atmosphere and product identity**, it is a baseline rather than an
arc, and it must never be cited as evidence of what this game feels like to play. **A dark menu
is not a scary menu, and the change in value must not be mistaken for a change in gate.**

**Camp campfire lighting — directed 2026-08-07 (Issue #152), `system` altitude, instrument only.**

The campfire starts unlit; a player leaves the fire pit, gathers wood in the dark, piles it, and
strikes a match to light it. Directed as a **system**, not a scene — per §10's new *refuge
maintained only by leaving it* row.

**Read the scope line first, because the gate results depend entirely on it.** #152 builds
**unlit → lit** and stops. The per-time-of-day arc, decay through the night, and the **stoke**
verb are Talon's own deferral (2026-08-07, Issue #153, blocked on #152). Every gate below is
scored against #152 *as it will actually ship*, not against the finished device — because scoring
a feature on its sequel is how a ledger starts lying.

| Device | Status in this level | Note |
|---|---|---|
| Refuge maintained only by leaving it | **instrument built, device not spent** | #152 is the mechanism; #153 is the spend. Do not log this as `spent-here` until safety actually runs out. |
| Day/night cycle as clock | **borrowed, not re-spent** | The fire's need is *pinned* to the existing dusk crossing and authors no second clock. **The fuel/lit state is not a clock and must never be built as one:** §2.2 defines a clock as visible, shared, monotonic and **inevitable**, and fuel is player-reversible by construction. It is §3.4 degradation wearing a clock's clothes. Stated in these words because a fuel bar is the single most likely thing for a later packet to mistake for a pressure driver, which would walk straight into §13's *one clock or two* fork by accident. |
| Night reversal | **leaned on, not resolved** | Same posture as hoodlab, the house, and camp L10. `MinAmbientEnergy` untouched. See the §13 amendment this direction adds — the fork's *shape* changes, its answer does not. |
| Fog as sightline denial / Colour temperature as threat channel | **untouched** | Both ride the dome's coupling to `CycleDriver.Phase`. The firelight is a *local* warm pocket and must not be built as a second global grade — that would double-spend the colour-temperature device (§6.6) on a channel that is already landing. |
| Wrong silence | **NOT spent — bed invested instead** | See the §10 row. The crackle is the setup. The crackle stopping is caused and visible, so it is not the device, and logging it as one would burn the strongest entry in the file on an event with an obvious explanation. |
| Flash beacon | **kept whole** | The match is a brief light event and could easily collide with the reserved ambiguous-position-broadcast device. It does not, on one condition the build must honour: **the match is struck at the pile, never carried as a light source.** A match used as a torch out in the woods *is* a position broadcast, and it would spend #108's device sideways. |
| Per-player sensory asymmetry | **produced, not spent** | §5.4 asymmetry falls out for free (the fetcher perceives the dark; those at camp perceive an absence). No `vfx-proximity` machinery is built, so the continuous-sensing device stays whole — same posture as the flash-beacon row above. |
| Synced audio-visual peak | **kept whole** | Ignition aligns audio and visual in one instant for every player, which is §4.6's shape. Deliberately **not** logged as a spend: ignition is a *release* (§3), it repeats every run, and §4.6 is reserved for a spike. Aligning a release across channels is craft, not a spend. |
| Cost-to-know instrument | **untouched** | Adjacent but not the same trade — see the §10 row's first sentence. |

**The five:** *belief* — the fire pit is a fixed property of the camp, like the ground, and the
place the dome closes on is simply *there*; *break* — the refuge turns out to be conditional on
somebody abandoning it, and the sharpest instant is **the first refused strike**: the match
flares, the pile is too thin, the flame dies, and the toll has already been paid. (Recorded
precisely because the weaker reading — a match that fails at random — is the one §7.2 forbids.
The refusal is deterministic and caused by the player's own too-early strike, which is what
makes it a break rather than a slot machine.) *why here* — the campfire is the one
landmark every player independently sights from every trailhead (`LEVEL-BIBLE.md` §3, §8.2,
§8.4) and the dome already converges on it, and the beat cannot move in time because five
minutes earlier there is no reason to bother and five minutes later it is done blind; *who
perceives* — everyone, in the same second, **differently** (§5.4), and the light bloom at
ignition is shared from wherever each player stands; *what changes* — the group learns the camp
has a maintenance cost, which is the belief #153 needs in place before it can charge one.

**Dread gate.** Ambiguity (§2.1) **pass, on loan** — the woods are ambiguous because they are
empty, which is *currently true*, so the fuel expires the run after players learn it; what this
feature spends is "is the camp safe" becoming a knowable variable. Clock (§2.2) **pass** —
borrowed, not authored. Break (§3.1) **pass, and it is this ledger's first** — the failed strike
is a real break, inside player causation (§4.5). Incompleteness (§3.2) **pass** — lighting
resolves tonight's fuel and leaves the night standing; it must not restore "the camp is safe" as
a permanent fact. Degradation (§3.4) **FAIL in #152** — once lit it stays lit; this is #153's
payload and is recorded as a named dependency, not a defect discovered late. Affect (§6.0)
**pass, conditional on one build requirement**: the fire's state must be legible from *distance*
and from *inside the woods*, so a fetcher can watch their own refuge from outside it. If the
state only reads when standing next to it, the feature is perceived and feels like nothing,
which is a §6 defect and not a §8 one. Fairness (§7.1) **pass** — fully avoidable with foresight.
(§7.2) **conditional — the one real finding**: a match that fails on unreadable RNG is
arbitrariness about *rules*, which §7.2 forbids in as many words and which is the exact line
between dread and frustration. **The match may fail; the failure must have a cause the player
can learn.** §7.4 anti-unwinnable: an unlit fire may never seal or unnavigate a run, and there is
no valid TBD on that.

**Spike gate: no spike claimed, and one is refused.** This beat repeats every run at a scheduled
point, which makes it §8.4's scripted set-piece if treated as a peak, and §4.4 caps a session at
roughly one true spike. Ignition is a release. **But the feature is unusually good spike
*substrate*, and that is the more useful finding:** the house-interior bedroom beat and the L10
night pass both fail §4.1.1 (stake) and §4.5 (agency) — recorded twice in §12 above — and a group
with somebody out in the dark on an errand they chose has manufactured both for free. Whatever
eventually threatens the camp should be pointed at this window rather than at a fresh one.

**Prohibitions, all nine.** §8.1 over-exposure — pass; nothing is exposed, and the guard is that
the woods must not become where the creature is *reliably* met, or the fetch becomes a scheduled
introduction. §8.2 startle as build — pass, and prohibited: the dark does the work, nothing may
jump at the fetcher as the engine of the beat. §8.3 reliable telegraphs — **pass by §8.3's own
parenthetical, with a guardrail**: the fire's state is a *state-change* cue and therefore a
fairness contract that must stay honest (`LEVEL-BIBLE.md` §8.2), not a dread hint that must
sometimes lie. So **the firelight must never be made to flicker to hint at the creature** — that
fuses the two channels §8.3 exists to keep apart. §8.4 scripted set-pieces — **risk, flagged**:
the trigger repeats identically, so the variation must live in the possibility space (who goes,
when, what has already happened), and nothing may be authored to fire at a fixed point in the
fetch. §8.5 illegible chaos — pass; stake and cause are both legible. §8.6 unrelenting tension —
pass, and structurally the feature's best property: it manufactures a valley. §8.7 permanent
safety — **FAIL in #152**, same standing failure as hoodlab, the house and L10, now with a named
resolution (#153) instead of an open dependency. §8.8 streamer-first — pass. §8.9 punitive
failure — **conditional**: losing the fire must sting and must never convert "we lost the fire"
into "we wasted an hour."

**What #153 stays reachable from, and what it would cost.** The state machine is authored open:
`Unlit → Laid → Lighting → Burning` with `Burning` deliberately non-terminal, so #153's
`Burning → Dying → Out` and a `Stoke` verb are additions rather than a rewrite. What #153 would
still have to bring: a fuel quantity with a drain (#152 stores fuel as a discrete laid-log count,
which is a countable #153 can decrement but not itself a timer), the per-time-of-day arc, and the
decision about whether an unlit fire at dusk is survivable — that last one is §6.2's, not this
entry's.

**Camp lake — directed 2026-08-08 (packet W5, against the approved water contract
`docs/superpowers/specs/2026-08-08-lake-water-contract-design.md`).** Altitude: **level** — the
lake as a zone of the camp — with a **system** pass (the swim/chill/go-under machine) nested and
flagged for a second look once somebody has actually been under it. Full working:
`docs/superpowers/direction/2026-08-08-lake-direction.md`. Nothing was built; this is direction
only, and W1–W4 were all unstarted when it was written.

> **⚠ Ledger fork, second occurrence** (the first was 2026-07-28, reconciled 2026-08-06). The
> unmerged live branch `feat/sound-integration` (head `5e567c1`) is based on an older trunk and
> carries exactly one Part II delta this base does not: **wrong silence moved `unbuilt → fresh`**
> on 2026-08-08, because `AmbientBed` (with `Withdraw`/`Restore`) was harvested from PR #73 and
> wired into the played camp world. Its own note is correct — that merge is a **deposit, not a
> spend**, and `Withdraw` has no caller outside `AmbientLab`'s F8 toggle. Conversely, this base
> carries the four 2026-08-07 entries and four device rows that branch lacks. **This pass
> deliberately did not edit the wrong-silence row** — it is the one line both branches would
> touch, and the claim is recorded here in prose instead so the merge stays trivial. Read the two
> together until they are reconciled.

| Device | Status in this level | Note |
|---|---|---|
| The unreadable dark | **deposit — NOT spent** | Its first host in a played world, released by the menu revision and picked up here. Water is unresolvable by physics and inert by contract (§1.1). An investment, exactly as the row says; nothing is ever withdrawn from the lake. |
| Wrong silence | **REFUSED — and this is the pass's most important refusal** | **Do not wire `AmbientBed.Withdraw()` to the water state machine — not to `WentUnder`, not to `Swimming`, not to submersion depth.** §6.3 needs an absence with *no explanation*; going underwater is the most explicable absence there is, and a cause defuses the device. Identical reasoning to the campfire entry's refusal of the crackle, with less ambiguity rather than more. What makes it urgent instead of academic is the fork note above: the mechanism now **exists** and is one line from callable. The correct build is a **bus-level lowpass on `Sfx` and `Ambient`, swept with submersion — a filter, never a gain.** Under the surface the world is filtered, not silenced. |
| Night reversal | **leaned on, not resolved** | Fifth consecutive camp entry with this posture; `MinAmbientEnergy` untouched. But the lake makes the contribution that row's own honest note has been waiting for — *"the reversal's substance is still unbuilt"*. At the lake the dark costs something concrete and entity-free: you cannot see below you, and the thing you cannot see is the medium you are in. Geometry supplies the substance; no threat is required. Recorded against the existing row rather than as a new one, because inventing a row for what a row covers is register inflation. **See the §13 amendment this pass adds — the fork gains a fourth side and a better venue, and its answer is still Talon's.** |
| Day/night cycle as clock | **borrowed, not re-spent** | The lake authors no clock and must not. **The chill is not a clock and must never be built as one:** §2.2 requires visible, shared, monotonic and *inevitable*, and chill is per-player, invisible to others, self-initiated and reversible by turning around. It is §3.4 degradation wearing a clock's clothes — the identical finding #152 recorded about the fuel state, restated because a `[0,1]` value with three escalating cue channels is the single most likely object in this contract for a later packet to mistake for a pressure driver. That mistake would walk into §13's *one clock or two* fork by accident. |
| Open sanctuary inverted by dark | **considered and NOT spent** | Tempting and wrong, and the reason generalises. §6.1 separates **prospect** from **refuge**; the day lake is prospect relief (the map spec's "one open-prospect relief axis, the anti-woods") and has never offered cover at any hour. That device is enclosed-and-safe becoming enclosed-and-hostile — the great hall's, salvaged to the Sleeping Cabin. The lake attempts no version and the device stays whole. |
| Canopy as spatial darkness | **not spent** | `spent-here` in the woods, whose entry forbids a second position-keyed darkness system in the camp. The lake's darkness is the *cycle's*; depth-tinting is a property of water, not a second field. |
| Landform as disorientation | **not spent; one agreement worth recording** | `spent-here` in the woods, and the lake is the opposite transaction — you always know which way the shore is. Note that the woods' compass (causal drainage: wet, dark, mossy means **down**) **points at the lake**, so the two passes agree by construction: follow water downhill and you arrive at the one place on the map with no cover at all. |
| Scale that is not the player's | **not spent** | `spent-here` in the woods. Open water has no scale objects at all, which is a different absence and not this row's. |
| Fog as sightline denial | **not spent** | That row's reuse rule is explicit — only where something is *approaching*. Nothing approaches at the lake. Haze over water, if W3 builds any, is a mechanism choice and not a spend. |
| Colour temperature as threat channel | **deliberately untouched — and this pass is where it was most at risk** | Water *wants* to go cold blue at night, and cool is the threat direction (§6.6). Same trap the land pass flagged, sharper here. **Requirement: the night water goes darker and lower-contrast, never bluer.** The dusk sweep's orange→plum→indigo grade stays the sole warning lane; the only hue the water may introduce is the one that reads as physics rather than warning. A second warning colour puts two channels in one lane and neither survives. |
| Forced separation | **invited, not forced — not spent** | A swim separates you and §5.4 asymmetry falls out free, but the whole group can wade in together, and §5.3's precondition is still unmet: togetherness is worth nothing while no threat exists. Identical posture to the campfire fetch. |
| Per-player sensory asymmetry | **produced, not spent** | The swimmer's camera sits at the waterline and cannot see over the water; the dock sees a head. No `vfx-proximity` machinery is built, so the continuous-sensing device stays whole. |
| Cost-to-know instrument | **untouched, and the cleanest statement of the lake's problem** | §2.3 trades safety for **information**. Swimming trades safety for *nothing*, because §1.1 guarantees there is nothing out there to learn. That is exactly why the water is currently a surface and not a place. |
| Refuge maintained only by leaving it | **untouched** | The campfire's, #152/#153. One note that strengthens it and must not be logged as a spend of it: `Soaked` clears only at the fire, which makes the fire a **destination** rather than a spawn point. |
| Teammate voice cut-out | **untouched — and flagged for W2** | Blocked on an afterlife channel. A swimmer's proximity voice going strange (waterline, muffled, thinning) is *adjacent* to §5.5 and must not be built as a version of it: §5.5 is a subtraction with no cause, and water is a cause. |
| Accumulating wrongnesses | **untouched, and explicitly NOT hosted** | The inverse of the two woods entries, deliberately. The lake is the one surface that must never host one — see the inoculation finding below. |
| Sound with no visible source | **untouched — a live temptation, refused** | A sound from out on the water with nothing there is the most obvious idea in this feature and it is an §8.1 event. Refused on the same grounds as everything else on that surface. |
| Synced audio-visual peak | **kept whole** | The go-under aligns audio and visual in one instant, which is §4.6's shape. Deliberately not logged: it is a release, it repeats every time, and §4.6 is reserved for a spike. Aligning a release is craft, not a spend. |
| Flash beacon / Route loss / A world that breathes / Persistent scarring / Tension-budget pullback / Non-predictive threshold / Doorway as proscenium / Stairs that lie / Rising tide / Proximity voice falloff | **untouched** | Nothing here touches, spends, leans on, or defuses any of them. |

**The register answer, stated plainly because the packet asked for it: the lake does not earn a
device of its own, and the fourth-dread-pocket reading is refused.** M3's woods own three pockets
(exposure / compression / blindness) and the night lake's best trick — open, black, nowhere to
hide, you can be seen — is the **burnt stand's**, which does it on ground a player has a reason to
be on. Three independent grounds, any one sufficient: it duplicates exposure; a pocket is a place
you go and §1.1 guarantees nobody goes here; and pockets are off-trail commitments while the lake
is on the main axis, visible from camp, next to the dock everyone already stands on. What the lake
gets instead is one **deposit** and **substance for a `seeded` device that had none** — which is
worth more than a fourth peak on a map §4.4 already thinks is near saturation.

**The structural finding, and it is what makes this pass buildable: the night lake's affect is
delivered from the DOCK, by players who never enter the water.** §1.1 removes every reason to swim
at night, so a register that requires entering is a register nobody receives. The dock — already
built by M5, already framed by the map spec §9 and never actually directed until now — is where a
player stands *over* the unresolvable surface without being in it: §6.1's prospect-with-no-refuge,
on walkable ground everyone reaches without choosing to. **One correction to the map spec's
framing: the dock at night is not a "dread device."** No clock, no threat, nothing approaching —
per §2.2 it is **creepy, not dreadful**, and calling it otherwise is the same overclaim the main
menu entry had to walk back. Two consequences that are requirements: the canonical night capture
position is the **dock tip, looking down and looking west** (if the water only works from a
swimmer's camera or an overhead view, it does not work), and the **dock creak is underfoot only,
never an ambient loop** — a player standing still on the dock hears the dock say nothing, which
puts them over an unreadable surface with no information at all. *That absence is by construction
and is NOT wrong silence; nothing was withdrawn because nothing was ever there.*

**The two registers of the go-under, and the craft instruction they produce.** By day it is
**comedy and a complete release** — §3.3's laughter, §5.6's failure-as-content, §9's absurd payoff.
It is allowed the full all-clear §3.2 normally forbids, because a relief valve that leaves residue
is not a valve, and that exemption is scoped to the day. At night it is **the identical event with
the release withheld** — not horror, which would invite the builder to make it feel like drowning
and would break both the contract's §0 and §8.9. The register changes by **subtraction**, and all
three subtractions belong to the level rather than to the effect: the audience is gone (§1.1), the
cost is real (the tape is the night loop's only currency), and *nothing beat you* — you were not
hunted or caught, you simply could not stay. The aftermath is the beat: `Soaked`, a ruined tape,
and a walk back to the fire. **So W4 ships ONE set of events. There is no night version of the
go-under** — no sting, no drone, no darker variant, no second preset (§8.2). The comedy is not
removed by adding horror; it is removed by removing the audience, and the clock already did that.

**The rope: set dressing today, and it must stay that way.** Spec §3.3's *"the same rope with
nobody behind it"* is right about what the rope will be and wrong about what it is. §2.1 needs a
belief to break, and nothing has ever installed *"the rope is a rule"* — the retired counselor framing's supervision was
deferred and the rope is not collision, so a player who has never been turned back has learned
nothing about it. More sharply: the rope is currently a `LEVEL-BIBLE.md` §8.2 **fairness** cue (it
honestly marks where the cold curve accelerates), and §8.3's own parenthetical forbids fusing that
lane with a dread lane. **So the rope does not flicker, glow, sway wrong or hint — it tells the
truth about the cold, day and night.** The day loop has to buy the rule before the night can spend
it; see the new *Authority withdrawn* row, whose first spend should be the sneak-out.

**The inoculation finding, third occurrence, and it applies to water verbatim.** The obvious idea
is that something should occasionally disturb the surface. Refused, on the main-menu entry's
argument unchanged: *a surface the player sees dozens of times, that reliably produces a wrongness,
teaches that wrongnesses are decoration.* The lake is visible from the camp, from the dock and from
both shoreline routes, every session, for the life of the build. Put an event on it and the ambient
rule of the world becomes *things happen in the water here and nothing ever comes of it* — at which
point the first real emergence anywhere is a shrug and *accumulating wrongnesses* has been defused
before it was ever spent. **Spending a device buys a moment; defusing one buys nothing.** This is
why the entry above records a deposit: an inert lake is worth more than an eventful one, and §1.1
already guarantees the inertness for free.

**Dread gate.** *Ambiguity* (§2.1) — **pass, and it deposits rather than spends**; conditional on
the shader, because a flat black plane resolves instantly as a flat black plane and the pass then
fails silently in a material. *Clock* (§2.2) — **FAIL**, and the finding is the chill row above.
Stated in the section's own words: **the night lake is atmospheric, not dreadful**, and the target
is set one rung down at §1 creepiness accordingly. *Break* (§3.1) — **pass for anyone in the water,
FAIL for the lake as a place, and the split is this pass's sharpest finding.** The swim contains a
real build and a real break (chill → go-under), inside player causation (§4.5) and
avoidable-but-barely (§7.1) on three redundant channels — more than the woods have. But §1.1
guarantees almost nobody is ever in the water at night to receive it, so the lake as a place a
player stands beside still has no break, for the fifth consecutive entry. **Neither half may be
quoted without the other.** *Incompleteness* (§3.2) — **pass at night, deliberately fail by day**,
and both are correct. *Degradation* (§3.4) — **pass structurally, and the cleanest instance in the
repo**: the cold curve steepens westward, so the safe window shortens geometrically as the player
commits and the player sets the rate. Third structural degradation the camp gets free from geometry
(canopy openings, the longer return, now the cold ramp) and, like both others, **it does not fix
§8.7**. *Affect* (§6.0) — **pass, conditionally, and the condition decides whether any of this
worked**: the lake must be felt from the shore by a player who never enters. The test is not "does
the capture look like water" but *does standing at the dock at 2 a.m. feel different from standing
at the dock at noon, for someone who never touches it.* If the only answer is "it is darker," this
is the same §6.0 defect Talon reported about the treeline, in a new place. *Fairness* (§7.1, §7.2)
— **pass, with two floors**: the cold curve is a pure function of position (learnable, consistent,
never lying), and **the shore must stay readable from the water at every darkness level** — the
water may be unreadable, the land may not. That floor **protects** §6.2 rather than resolving it,
by keeping a shader from pre-empting Talon's choice. §7.4's anti-unwinnable guarantee has no valid
TBD and the contract's own headless test carries the mechanical half.

**Spike gate: no spike claimed, and one is refused.** The go-under fails a *different* condition in
each phase — by day **stake** (nothing is on the line, which is exactly why it is funny), at night
**simultaneity** (§1.1 guarantees nobody is there). Neither clears all four, so §4.4's per-session
ceiling is untouched. The day version is an excellent **release** and should be built as one;
staging either as a peak would be §8.4's scripted set-piece and §4.4's inflation in one move.

**Prohibitions, all nine.** §8.1 over-exposure — n/a, and enforced as a **hard refusal**: nothing
is ever shown in or on the lake — no shape, no breach, no wake, no silhouette, no uncaused ripple,
not once, not rarely, not as an easter egg (the inoculation finding above). §8.2 startle as build —
**live constraint in three places**: no night sting on the go-under; the submersion filter sweeps
and never snaps; and the deleted invisible wall must not be replaced by a sudden depth change (the
contract already smoothsteps the bed — this is the affect reason). §8.3 reliable telegraphs — **not
violated**, with the rope and the three chill cues held in the honest §8.2 lane, plus **one forward
flag**: the map spec places shoreline evidence candidates at the lake's north and south landward
corners, and the first hunt-seed pass that makes the shoreline *reliably* where evidence is turns
the water's edge into a telegraph. That is a constraint on M4, recorded here because the packet
that breaks it will not be this one. §8.4 scripted set-pieces — **not violated, live risk**: the
go-under fires identically every time, which is tolerable for a release and becomes a violation the
moment W4 authors a beat *inside* it (a fixed camera move, a scored moment, a staged view). §8.5
illegible chaos — **the live risk, and it is a rendering risk**: unresolvable and illegible are one
dial apart. Sparse hard glints on a dark field is unresolvable (the eye keeps trying); dense fine
sparkle on a dark field is noise (the eye gives up). Third form of the same risk the canopy and
land passes each named. §8.6 unrelenting tension — n/a and structurally healthy; the lake is the
relief axis by day and the walk back to the fire is a valley. §8.7 permanent safety — **FAIL,
carried forward** for the fifth consecutive entry. **One thing this pass changes, recorded and not
claimed:** `Soaked` is the first standing cost the map has ever charged the player's own *body*,
cleared only by returning to the fire and standing still. Not sanctuary degradation, and it does
not fix §8.7 — but it is the cheapest honest substrate #153 will find. §8.8 streamer-first — pass,
with a named temptation: a splash is the most clippable object in this feature. The test, borrowed
from the menu entry — *is this splash sized for the player who is cold and alone at 2 a.m., or for
the trailer?* Day may be generous; night may not. §8.9 punitive failure — **pass, and the
contract's best decision** (tape not camera, sputter not death), with a **forward constraint for
the camcorder packet** *(the camcorder is retired — canon §3, 2026-08-08 — but the constraint is carrier-agnostic and now lands on the Pot's tribute loop, canon §1.5)*: a tape must never represent more than one night's work, or "we lost the
tape" becomes "we wasted an hour" and the valve inverts.

**One new fork added to §13** (proposed with its trigger, not surfaced), and **one existing fork
amended** — §6.2 gains a fourth side and a better venue. Neither is resolved here. `ART-BIBLE.md`
§4.4 is untouched: the direction document gives directions of travel for one surface and is
explicitly **not** a material or lighting law, and the previz-era "laws" (AgX, no-emission, flat
warm/cool) remain unratified and were not cited as though they were.

**Amended 2026-08-08 (P2, playtest fallout) — the honest §8.2 lane retuned, no device touched.**
Altitude: **system**, nested under this entry exactly as the original pass reserved. In the field
a real player swam, floated, and never perceived any of the three chill cues — with audio on. Not
a directorial failure: this system was always outside dread's remit (see the *Day/night cycle as
clock* row above — chill is explicitly not a clock, and §8.3's parenthetical keeps this fairness
lane separate from any dread lane), so the five questions in §4 do not apply and are not forced
here. The fix is entirely inside the lane the original pass already drew:

- **Onset moved from chill 0.35 to 0.05.** The prior value let a player float up to 63 s inside
  the rope with zero signal before the first one — which is the §6.0 defect stated plainly
  ("a cue that is perceived and feels like nothing is a §6 defect") arriving as *nothing perceived
  at all*. §8.2 requires the cue reliable, not subtle; this pass chose legibility over the
  original's "never punish a paddle" instinct, deliberately, because the field report proved the
  instinct was costing the fairness contract itself.
- **Haptic demoted from counted to bonus.** Controller rumble is silent by construction with no
  controller connected, which is most of this codebase's own dev/test posture and plausibly the
  field session too — so the shipped "three redundant channels" was, in practice, two for anyone
  on keyboard and mouse. A fourth file, `ChillReadout.cs`, adds a diegetic worded readout (three
  stages, never a number — spec §5.1's "no meters" ban holds) as the channel that is always on
  screen, and rumble keeps running as a bonus fourth signal rather than being deleted.
- **Audio's onset volume raised** (-22 dB → -16 dB) for the same reliability-over-subtlety reason.

**Prohibitions, re-checked, none newly at risk.** §8.3 — still not violated; the readout reads
`WaterService.ChillOf` directly, so it can never fire without the real value rising, and it never
warns of something that then fails to arrive. §8.5 — the opposite of illegible chaos: a worded,
monotonic, single-cause readout is the most legible object this pass could add. §8.1/§8.4/§8.6/
§8.7/§8.8/§8.9 — untouched, and this pass touches nothing that could move them.

**No device row added or changed.** The three chill cues are not a THRILL device and were not
treated as one — they remain `MECHANICS-BIBLE.md` §10.4 / `INTERACTION-BIBLE.md` §8.2 territory,
composed by this file rather than owned by it, exactly as the 2026-08-08 W5 pass established.

**Handed off, unanswered:** whether haptic should ever be re-promoted to a fully-counted third
channel (behind a controller-connected check) is a scope call for whoever next touches this
system, not a directorial one — not answered here.

**Camp — the panic drop, directed 2026-08-09 (packet R4).** Altitude: **system** — one verb, one
state machine, and the question of what feeling it manufactures at all. Not `scene`: this fires
many times a night, at no authored position, so directing it as a placed beat would be §8.4's
scripted set-piece in advance. The packet asked for "the release beat as the centrepiece" and the
centrepiece is real, but it is a **recurring peak produced by a system**, not a moment on a map.

**Read the dependency first, because every gate below turns on it.** R4 builds the component and
nothing else. The watcher (M2a) is a separate packet and does not exist yet. **A panic button with
nothing to hide from is not a horror mechanic, it is a posture toggle**, and the honest statement
of R4 shipped alone is that it manufactures no dread whatsoever. That is not a defect in this
packet; it is the order the work was cut in. Every "pass" below is scored against the verb *with a
watcher present*, and marked where it is not.

| Device | Status in this level | Note |
|---|---|---|
| The blind hide | **instrument built, device not spent** | R4 is the mechanism; the spend is the first night a watcher is out there while somebody is down. Do not log `spent-here` until then. |
| Cost-to-know instrument | **leaned on hard, NOT spent** | See the amended §10 row. The *trade* now exists; the row's continuous-sensing instrument does not, and no `vfx-proximity` machinery is built here. |
| Per-player sensory asymmetry | **produced, not spent** | §5.4 falls out for free and **inverted from every prior instance**: at the lake and the campfire the person in danger perceived *more* than the group. Here the prone player perceives least of anyone, which is the sharpest asymmetry the game has produced and the best vector §5.2 has ever had. Still no machinery built; the device stays whole. |
| Wrong silence | **REFUSED — fourth occurrence, and the most one-line-away yet** | **Do not wire `AmbientBed.Withdraw()` to the prone state — not on entry, not on depth of panic, not on the recovery.** §6.3 needs an absence with *no explanation*, and a player pressing their face into the dirt is a more explicable absence than going underwater was. A cause defuses the device. Same ruling as the campfire crackle and the lake submersion, for the third time: if prone changes the audio it is a **filter, never a gain** — the world goes muffled and close, it does not go quiet. |
| Day/night cycle as clock | **borrowed, not re-spent** | Panic authors no clock and must not. **Prone time is not a clock**, for the third time in this ledger after the campfire's fuel and the lake's chill: §2.2 requires visible, shared, monotonic and *inevitable*, and time-spent-down is per-player, invisible to others, self-initiated and reversible by standing up. It is §3.4 degradation wearing a clock's clothes. Stated in these words because a panic meter is now the single most likely object in the MVP to be mistaken for a pressure driver, and that mistake walks into §13's *one clock or two* fork by accident. |
| Night reversal | **leaned on, not resolved** | Sixth consecutive camp entry with this posture. `MinAmbientEnergy` untouched, and this pass adds a guard rather than a side: **the player's visibility floor and the ambient light floor are different quantities and must never be wired to each other.** One is how well the watcher sees the player; the other is how well the player sees the world. Fusing them would let a shader change silently retune the game's only defence, and would let R4 pre-empt Talon's §6.2 choice from a direction nobody is watching. |
| Teammate voice cut-out | **untouched — and flagged, same posture as the lake's W2 flag** | A prone player's voice going strange, close, or breathless is *adjacent* to §5.5 and must not be built as a version of it. §5.5 is a subtraction with **no cause**; panic is a cause, and the most legible one in the game. |
| Refuge maintained only by leaving it | **untouched** | The campfire's (#152/#153). The blind hide is portable and personal; that one is a place and communal. Different rows, and the new row's own note says why. |
| Accumulating wrongnesses | **untouched, and explicitly NOT hosted** | Nothing may be authored to happen *while* the player is down. A prone player who reliably hears or half-glimpses something has been handed a fourth-occurrence inoculation: the ground becomes where wrongnesses live, and they become decoration. The ground must be as inert as the lake. |
| Forced separation | **invited, not forced — not spent** | Identical posture to the campfire fetch and the lake. One player down and one standing is separation the group chose. |
| Startle as build | **prohibited, see §8.2 below** | Named here because the release is the single most tempting place in the MVP to put a sting. |
| Flash beacon / Route loss / A world that breathes / Persistent scarring / Tension-budget pullback / Non-predictive threshold / Doorway as proscenium / Stairs that lie / Rising tide / Proximity voice falloff / Canopy / Landform / Scale / Open sanctuary / Fog / Colour temperature / Unreadable dark / Authority withdrawn / Synced audio-visual peak / Sound with no visible source | **untouched** | Nothing here touches, spends, leans on, or defuses any of them. Colour temperature and the canopy are called out explicitly because a "panic vignette" would be the obvious way to build the prone view and would put a second grade in the dusk sweep's lane — **the prone view attenuates and occludes; it does not re-tint.** |

**The five.** *Belief* — **perception is free.** Not "I am safe" — the deeper, never-examined
assumption every game has taught this player, that looking costs nothing and you can always look
again. *Break* — the instant you go down, safety arrives and sight leaves in the same frame, and
they turn out to have been one resource all along. The **sharper** instant is the second one, and
it is the beat this packet is really about: wanting to know whether it is gone, and discovering
that the only instrument for finding out is the thing that makes you visible. *Why here* — it
cannot move. It is the only counterplay to a threat whose weapon is its eyes, so the verb and the
threat are the same idea seen from two ends; and it has to live on the firewood walk because that
is the only errand that puts a **cost on the ground beside you** when you drop. *Who perceives* —
everyone, in the same second, and the prone player perceives **least**, which is the inversion
noted in the table above. *What changes* — the wood is on the ground where you fell, and the group
now has a decision (go back for it, or eat the loss) that did not exist a second earlier.

**Three directions that are requirements, not tuning.**

1. **The exit must not deliver an all-clear.** Standing up may tell you whether it is looking at
   you *now*. It must never tell you it has gone. This is §3.2 and it is free — the watcher's own
   contract is *stand, watch, be gone when you look back*, so the ambiguity is already paid for.
   The failure mode to watch is the opposite of the obvious one: a recovery that reliably resolves
   the question turns the button into a **scanner**, and the player will use it to farm certainty
   rather than to survive.
2. **The dropped wood stays where it fell, and stays findable.** It is a §6.5 **trace** — and the
   first one in the game authored by the *player's own fear* rather than by a level designer. A
   panic that despawns its cost erases the only lasting evidence the beat ever happened, and
   §8.9's floor applies with it: losing the wood must cost the errand and never the session.
3. **The recovery must be felt as duration, through the body — never read.** No meter, no bar, no
   number, no countdown. §6.0 is explicit that a cue which is perceived and lands nowhere is this
   file's defect, and the packet's own hardest question ("what tells the player, from the ground,
   in the dark, with a terrible sightline, that the button is worth trusting?") has a wrong answer
   that is very attractive: *make the watcher's reaction legible from prone.* **Refused** — that
   restores prospect, and prospect is the exact thing the hide is spending. See below for what the
   answer actually is.

**The answer to the packet's hard question, because it is the one thing here that is not obvious.**
Trust in this button is not built from the ground. It cannot be — the ground is blind by
construction and any fix that makes it less blind unbuilds the device. Trust is built **afterward**
(§4's fifth question) and **socially** (§5.1): you learn the button works by having used it and
survived, and by your friends telling you what they saw while you could not see. **The teammate is
the sightline.** That costs nothing to build — proximity voice ships and works — and it converts
§5.4's asymmetry from a side effect into the mechanic's own trust channel. It also gives the
standing player something real to do with their exposure, which is the group-strength §5.3 asks for
before separation can mean anything.

Two guards on that, and the second is a routed dependency, not a direction:

- **The solo/silent floor.** A player alone, or one whose friends are also down, must still have
  *something*, or §7.1 breaks for exactly the people already having the worst time. The honest
  channel that does not restore prospect is the player's **own body** — you perceive your own
  stance and your own recovery, felt as elapsed time. That is requirement 3, and it is why
  requirement 3 is a requirement.
- **Whether the watcher perceives voice at all is NOT this file's call and is not answered here.**
  If speaking while a friend is down should carry risk, that is a capability on the watcher, and
  §0 is explicit that directing a feeling never grants one. Routed to M2a / `/spec-entity`.

**Dread gate.** *Ambiguity* (§2.1) — **pass, and with a watcher present it is the strongest
instance in the ledger**, because the uncertainty is not withheld by the designer but purchased by
the player, which is §2.3's whole argument for why that form is worth more. Conditional entirely on
requirement 1. *Clock* (§2.2) — **pass by inheritance only**; borrowed from the cycle and the fire,
and this system authors none (see the table). *Break* (§3.1) — **pass with the watcher, FAIL
without it, and R4 ships without it.** This is the ledger's second real break candidate after the
campfire's refused strike, and unlike that one it is a break caused by *a threat* rather than by a
rule. Stated plainly so R4 is not read as having delivered it. *Incompleteness* (§3.2) — **pass,
conditional on requirement 1**, which is the sharpest single line in this entry. *Degradation*
(§3.4) — **FAIL as specified.** Nothing in the contract makes the hide finite except the physical
fatigue of holding a button, and fatigue is an accident of input hardware, not a design. See §8.7
below and the new §13 fork. *Affect* (§6.0) — **pass, conditional on requirement 3.** The test is
not "does the capture show three distinct stances"; it is *does the second before standing up feel
longer than the second after.* If the recovery only reads as a number or a pose change, this is the
same §6.0 defect Talon reported about the treeline, in the most important place yet. *Fairness*
(§7.1) — **pass**: avoidable but barely, and the player picks the moment, which is the best shape
this gate takes. (§7.2) — **pass, with one floor: visibility must be a pure, learnable function of
stance, light and motion, with no randomness anywhere in it.** A hide that sometimes fails for
reasons the player cannot reconstruct is arbitrariness about *rules*, which §7.2 forbids in as many
words and which is the exact line between dread and frustration. §7.4 anti-unwinnable — **a hard
requirement**: no input, no state, no interruption may leave a player unable to stand up. A panic
you cannot exit is a sealed run wearing a mechanic's clothes, and there is no valid TBD on that.

**Spike gate: no spike claimed, and one is refused.** The verb fires many times a night, so staging
it as a peak is §4.4 inflation and §8.4's set-piece in one move. **But it is the best spike
*substrate* the ledger has recorded**, and better than the campfire's: stake (§4.1.1) and agency
(§4.5) are both manufactured for free by the player's own choice to go down, simultaneity (§4.1.3)
is available whenever more than one person is out, and there is a legible (§4.1.4) failure shape
sitting right there — **the player who panics and drops the group's firewood, in front of the
group.** That is §3.3 laughter and §5.6 failure-as-content in the same instant, and it is the
absurd payoff §9's ratio question keeps asking for without anyone having to invent one. Flagged as
substrate, deliberately **not** claimed as a spend, and whatever eventually threatens the camp
should be pointed at this window rather than at a fresh one.

**Prohibitions, all nine.** §8.1 over-exposure — **pass, and a hard refusal**: nothing is shown to
a prone player. No half-glimpse, no silhouette at the edge of the ground view, not rarely, not as a
reward for patience. §8.2 startle as build — **the live constraint, and the most tempting violation
in the MVP**: there must be **no sting on standing up**. Not a sound, not a cut, not a flash. The
dread is the anticipation of standing; a bang at the moment of standing replaces it with a reflex
and §8.2 is explicit that startle is a release and a weak one. §8.3 reliable telegraphs — **not
violated, with one forward flag**: the recovery window must not become a reliable all-clear
(requirement 1), because a cue that reliably means *safe* teaches the player when to relax just as
surely as one that reliably means *danger*. §8.4 scripted set-pieces — not violated; a verb is a
possibility space, and nothing may be authored to fire at a fixed point inside a panic. §8.5
illegible chaos — **pass, and structurally this system's best property**: one button, one rule, one
cost. §8.6 unrelenting tension — pass; the hide *is* a valley, and a short one. §8.7 permanent
safety — **FAIL, sixth consecutive entry, and this is the first time the ledger's standing failure
is PORTABLE.** Every prior instance was a place (the house, the cabin, the camp core) that a
designer could later charge for. A hide the player carries, that costs nothing and never degrades,
cannot be charged for by level design at all. The 5–15 s bound in R4's brief is what currently
prevents it, and that bound is enforced by a tiring thumb rather than by a decision — which is
precisely what the toggle fallback would remove. §8.8 streamer-first — pass, with a named
temptation: a prone-cam is a very clippable frame, and the test is *is this view for the player on the ground at 2 a.m., or for the trailer?* §8.9 punitive failure — **pass, conditional on
requirement 2**: dropping the wood must sting and must never convert "I panicked" into "we wasted
an hour."

**One new fork added to §13** (proposed with its trigger, not surfaced to Talon), and no existing
fork resolved. §6.2 is not moved.

**Handed off, unanswered:** every duration and every visibility number (playtest, Talon — the
director does not pick values); whether the watcher perceives voice, and whether speaking for a
downed friend should carry risk (M2a / `/spec-entity` — a capability, not a feeling); how the
visibility contributors combine into one score (`MECHANICS-BIBLE.md`); and the camera's behaviour
during panic, which R4 correctly defers to the in-flight clip-into-head fix — with one direction
attached for whoever builds it, since a seam is being left and it may as well be left pointing the
right way: **if the camera is ever pulled in during panic, it must be built as a loss of prospect,
not as a gain of intimacy.** Close is not the point; *unable to see past the grass* is the point.

**Camp night pressure — directed 2026-08-09 (packet R5, "the night closes in").** Altitude:
**system** — a pressure driver, i.e. a clock, which §2.2 names as the emotional keystone. Not
`level`: the camp's composition is untouched by this pass and nothing moves. Not `scene`: this
authors no beat and no moment. The lower altitude the higher one would delegate to (what the
firewood walk should feel like *under* a shrunken light) is flagged for a second pass and is
explicitly not answered here.

**Feeling.** *The room you are allowed to be a person in is smaller than it was last night, and
you can see exactly how much smaller.* Not fear and not startle — the target is dread's
**certainty half** (§1), supplied spatially. The player is meant to walk out to the fire on
night three, look at the ground, and know without being told that there are two of these left.
It has to be there because the camp is the one place every player independently sights from
every trailhead (`LEVEL-BIBLE.md` §3, §8.2, §8.4), so it is the only surface in the game where a
shared, glanceable, no-UI reading of the run's remaining length is even possible.

**The five.** *belief* — the camp is the part of the world that belongs to you; the woods are
where it stops, and that line is a property of the map. *break* — the line turns out to belong
to the night, not to the map, and it has been moving inward the whole time; the sharpest instant
is **the first night somebody notices a landmark they used to stand in the light beside is now
outside it** (the cabin at r = 18 m, which is lit on nights 1-2 and dark from night 4). *why
here* — it cannot move in space (only the fire has this property) and it cannot move in time
(before night two there is no yesterday to compare against, and after night four there is
nothing left to lose). *who perceives* — everyone, in the same second, and **identically**, which
is the deliberate opposite of the fetch's §5.4 asymmetry: this is the one fact the whole group
reads the same way, which is what lets them argue about it. *what changes* — the group acquires
a shared unit of "how much game is left", and every later decision (go now, go together, don't
go) is priced against it.

| Device | Status in this level | Note |
|---|---|---|
| The ration that never refills | `spent-here` — **and this is its first spend anywhere** | The camp owns this device now. Do not add a second monotone spatial ration to this level; a second one would not read as more pressure, it would make both unreadable. |
| Night reversal | **leaned on, not resolved** | Same posture as hoodlab, the house, camp L10 and #152. `MinAmbientEnergy` untouched. The driver writes no ambient, no fog and no grade — see the adapter's own class doc. |
| Fog as sightline denial | **borrowed, not re-spent** | The dome already couples fog to its front across the dusk sweep. This driver deliberately **holds at the night's opening value through the entire dusk band** so its motion never overlaps the dome's — two contractions in one window compete for the same read, and the dome's front speed is load-bearing for an escapability contract this pass must not perturb. |
| Colour temperature as threat channel | **untouched** | The adapter states one colour (warm, at the fire) and nothing else. §12's #152 entry already forbids the firelight becoming a second global grade; this obeys it. |
| Day/night cycle as clock | **borrowed, not re-spent** | No second clock is authored. The pressure derives from `CycleDriver`'s replicated phase and adds no counter of its own — which also closes `MECHANICS-BIBLE.md` §10.3's fourth pitfall by never having a second counter to desync. |
| Refuge maintained only by leaving it | **NOT touched, and must not be fused with this** | #153's fuel is the spend of that device. Fuel is player-reversible; this ration is not. If both end up driving the same light they must stay separable — pressure as ceiling, fuel underneath — or #153's device and this one collapse into one unreadable quantity and neither lands. |
| Wrong silence | **untouched** | Nothing here withdraws anything audible. |
| Tension-budget pullback | **untouched** | No breather is granted or withheld by this pass. |

**Dread gate.** Ambiguity (§2.1) **pass, and thin — the honest reading is that this device
supplies the frame for ambiguity rather than the ambiguity itself.** What the shrinking pool does
is make the boundary of the knowable a visible, moving object; what is past it still has to be
genuinely unresolvable, and that is `The unreadable dark`, which is deposited at the lake and
**not built at the camp**. Named as a dependency, not claimed as a pass. Clock (§2.2) **pass, and
it is the strongest clock in the file** — visible, shared, monotonic, inevitable, *and* the only
one that does not reset. Break (§3.1) **FAIL, by construction, and stated rather than hidden**:
R5 ships a build with no release in it. A contraction that never breaks is §8.6's numbness on a
five-night timescale. The break is not this packet's to author and must come from the walk, the
watcher, or the fire — but **something must break against this or the device decays into
wallpaper by night three**, and that is the single most important finding on this page.
Incompleteness (§3.2) **n/a** — nothing is released, so there is nothing to leave standing.
Degradation (§3.4) **pass, and it is this ledger's first structural one.** Every prior sanctuary
in §12 fails §8.7; this one degrades by construction and cannot be restored by any player action.
Affect (§6.0) **pass, conditional on one build requirement, stated as constitutive in the §10
row**: the pool must have a findable edge. A perceivable cue that feels like nothing is a §6
defect, and an unbounded falloff is exactly that. Fairness (§7.1) **pass** — the cue is the
earliest and most legible warning in the game, available before anyone leaves camp. (§7.2)
**pass, conditional on determinism, and pinned as a test rather than a hope** — a radius that
varied run to run would be arbitrariness about rules.

**Spike gate: no spike claimed, and one is refused.** This is a build, not a beat; it changes
continuously and repeats every night, which makes it §8.4's scripted set-piece the instant it is
treated as a peak. It is **substrate**: it manufactures §4.1's *stake* for free and for the whole
group, which is the condition both the house-interior bedroom beat and the L10 night pass are
recorded as failing.

**Prohibitions, all nine.** §8.1 over-exposure — pass; nothing is exposed, and the guard is that
the shrinking light must never be used to *reveal* the watcher at a threshold. §8.2 startle as
build — pass, and prohibited: the contraction is continuous and must never step, snap or
lurch to produce a jolt. §8.3 reliable telegraphs — **pass, and this is the pass with a knife in
it**: the lit radius is a §8.2 *urgency* cue and therefore a fairness contract that must stay
honest, so it may **never** be made to flicker, dip or vary to hint that something is out there.
That would fuse the two channels §8.3 exists to keep apart, and it is the same guardrail #152
recorded for the firelight — now with a second writer, which is exactly when a guardrail gets
broken by accident. §8.4 scripted set-pieces — pass; the curve is a continuous function of the
clock with no authored events on it at all. §8.5 illegible chaos — pass; one monotone quantity,
one cause. §8.6 unrelenting tension — **the live risk, and the counterpart of the §3.1 fail**:
five nights of build with nothing breaking against it is the documented way this game gets
boring. §8.7 permanent safety — **pass, and the ledger's first** — see the §3.4 note. §8.8
streamer-first — pass. §8.9 punitive failure — **not touched**: this driver removes light, never
progress, and it must stay that way; a shrinking radius that also confiscated something would
convert "the nights got hard" into "we wasted an hour".

**Handed off, unanswered (implementation, not direction):** how the edge is rendered so it reads
as an edge at 40 m in the dark and does not band on the GTX 970 floor (`vfx-lighting` /
`vfx-particles`); whether the pool's *interior* should also lose brightness as it loses reach, or
only reach (a feel call, and the driver deliberately holds brightness constant so the question
stays open); and what breaks against this build — routed to whoever directs the firewood walk,
which per the MVP plan's §6 has still never been run through `/direct`.

**The watcher — directed 2026-08-09 (packet R3), `system` altitude, instrument only.** Altitude
is **system** and not scene, because R3 builds the entity's behaviour and its look and places it
in no level: what feeling the system manufactures at all is the question, and the *scene* pass
(the first sighting, on the firewood walk, at a position somebody chose) is nested under this one
and flagged for a second look the moment it is placed. Nothing here directs where it stands.

A creature appears beyond the lit radius, faces whichever player is most visible, holds, and is
gone when you look away and back. It never pursues, never damages, never enters lit ground.

**Read the scope line first, because every gate below depends on it.** R3 ships **look, track,
leave** and stops. There is no raid, no cost, no consequence of any kind — canon's *"what the
creatures take when the fire goes out"* is an open call for Talon and is not this packet's to
invent. Scoring this feature on the sequel it obviously wants is how a ledger starts lying.

| Device | Status in this level | Note |
|---|---|---|
| The watched | **instrument built, device not spent** | R3 is the mechanism. It is not spent until a sighting happens in a played session at a position somebody directed. Do not log `spent-here` on the strength of a lab capture — that is the hoodlab mistake this ledger already records twice. |
| Attention as a resource the group can move | **instrument built, device not spent** | Same posture, and it additionally needs the panic drop (M3) to exist before a player has any way to *give* attention away. Half the device is in another packet. |
| The unreadable dark | **drawn against — NOT spent, and the two existing deposits are untouched** | This is what the deposit was for; see the §10 row. **The camp lake's deposit and the menu's inert backdrop are explicitly off limits** — no watcher on, over, beside or reflected in the water, and none in the menu woods, ever. Both surfaces are seen dozens of times per session and the third inoculation finding refuses events on them for exactly the reason that would apply here hardest. |
| Accumulating wrongnesses | **untouched — and this pass inverts §6.5's order, which is a finding rather than a defect** | §6.5 wants the thing introduced by *traces* before it is ever shown. That row is `unbuilt`, hosted twice (canopy, land) and never built, so as things stand **the watcher is the introduction** and the trace arrives afterward if it ever arrives. Recorded, not blocked on: doctrine order is a preference, not §7.4's anti-unwinnable guarantee, and the MVP exists to find out in one week whether this game is the one Talon is describing. The cheap fix if anyone wants it later is one trace in the woods before night three, not a redesign. |
| Night reversal | **leaned on, and this is the substance the row has been waiting for** | Sixth consecutive camp entry with this posture and `MinAmbientEnergy` is untouched again. But the row's own honest note — *"the reversal's substance is still unbuilt… being caught still costs nothing"* — gets its first real answer here, and it is a different answer from the lake's. At the lake the dark costs you the ability to see the medium you are in; here **the dark is where the thing that looks at you is allowed to stand, and light is the only thing that forbids it.** That is the reversal stated as a rule rather than as a palette, and it needs no threat capability to deliver. §6.2's fork is not moved one inch. |
| Day/night cycle as clock | **borrowed, not re-spent** | The watcher authors no clock and must not. **The dwell timer is not a clock and must never be built as one:** §2.2 requires visible, shared, monotonic and inevitable, and dwell is invisible, per-encounter, per-player and ended by the player's own behaviour. This is the third time this file has had to write that sentence — the fuel state (#152) and the chill (W5) were the first two — and it is written again because a countdown attached to a threat is the single most convincing counterfeit clock available. |
| Refuge maintained only by leaving it | **strengthened, not spent** | #152/#153's device. The watcher gives the fire's radius its first **predator-defined** meaning: lit ground is no longer merely the bright part of the map, it is the ground the thing may not stand on. That makes the fire burning down a *territorial* loss rather than a lighting one and it costs this packet nothing — but it is #153's spend, not R3's, and logging it here would claim a decay curve that does not exist. |
| Colour temperature as threat channel | **deliberately untouched — and this pass is where it was most at risk, for the third consecutive time** | The land pass flagged the damp hollow, the lake pass flagged the night water, and both were *surfaces*. This is a pair of glowing eyes, which is the single most tempting object in the game to make red or amber, and doing it would put a second warning colour in a lane the dusk sweep already owns. **Requirement: the eyes and the body are VALUE, not hue — achromatic, pale, distinguished from the dark by luminance alone.** The dusk sweep's orange→plum→indigo grade stays the sole warning lane. |
| Fog as sightline denial | **not spent** | That row's reuse rule is explicit: only where something is *approaching*. Nothing approaches here — that is the entire premise — and if fog or haze ever separates the watcher from the treeline behind it, that is a mechanism choice and not a spend. |
| Canopy as spatial darkness / Landform as disorientation | **not spent, and both filed forward flags against this exact packet — both are honoured, in code** | The canopy entry: *"the first packet that spawns a creature preferentially in the darkest cover turns the canopy into a reliable telegraph."* The land entry: the same sentence about hollows. R3 is that packet, it is the first one that could have broken either, and the constraint is now a positive requirement rather than an avoided mistake — **appear position may not correlate with cover density or with terrain concavity.** See §8.3 below. |
| Wrong silence | **REFUSED, and the refusal is the same one twice already made** | The obvious idea is that the bed cuts when it appears. §6.3 needs an absence with *no explanation*, and a creature standing at the treeline is the most complete explanation an absence could have — a cause defuses the device, exactly as the campfire's crackle and the lake's submersion each did. Worse here than in either: this one would also make the bed a **reliable telegraph** for the watcher (§8.3), fusing the strongest device in the file into a lane it must never enter. **Do not wire `AmbientBed.Withdraw()` to anything the watcher does.** |
| Flash beacon | **untouched, with one guardrail** | The eyes are a light event at distance and could collide with #108's reserved ambiguous-position broadcast. They do not, on one condition: **the eyes are a property of the creature's facing, never an emitted pulse** — they do not flare, blink to attract, or brighten on acquisition. A pair of eyes that *signals* has become a beacon and would spend #108 sideways. |
| Per-player sensory asymmetry | **produced in its strongest form yet, still not spent** | §5.4's asymmetry is no longer a side effect of position — it is **authored by the threat itself**, which picks one player and looks at them. One player's certainty against three players' doubt is the sharpest asymmetry this repo has ever had, and it costs no `vfx-proximity` machinery, so the continuous-sensing row stays whole. |
| Cost-to-know instrument | **untouched, and named as the packet this device is finally reachable from** | §2.3 trades safety for information. Coming up off the ground to learn whether it is gone is that trade exactly, and it is the FNAF door-check the pivot plan already identifies as the scariest moment in the MVP. The trade does not exist until the panic drop (M3) does — the watcher supplies the *reason* to look and the other packet supplies the *cost*. Recorded so whoever builds M3 knows the two halves are one device. |
| Forced separation / Route loss / A world that breathes / Persistent scarring / Synced audio-visual peak / Sound with no visible source / Doorway as proscenium / Stairs that lie / Non-predictive threshold / Scale that is not the player's / Open sanctuary inverted by dark / Authority withdrawn / Rising tide / Proximity voice falloff / Tension-budget pullback | **untouched** | Nothing in this pass touches, spends, leans on, or defuses any of them. |

**The five.** *Belief* — **the dark past the firelight does not resolve into anything**, and the
repo has spent three passes and two refusals installing precisely that habit: the menu's inert
backdrop, the lake's contractually empty surface, the canopy's unreadable depth. The player
believes, correctly and from experience, that nothing out there ever turns out to be anything.
*Break* — it resolves. Once. And the instant is **not the appearance**, which nobody sees,
because the player was not looking when it happened; it is **recognition** — the moment the eye
finishes parsing *dog* and then the legs fail to agree. *Why here* — because the treeline is the
only place on the map where the habit exists to break. It cannot move indoors (no habit was ever
installed there), it cannot move to the water (contractually inert, and the third inoculation
finding refuses it), and it cannot move into camp (it may not stand on lit ground, which is the
rule, not a shyness). The beat is nailed to the one surface the deposit was made on. *Who
perceives* — **deliberately not everybody**, and this is the design rather than a shortfall: it
faces one player, and the others see a body, a back, or nothing at all. *What changes* — the dark
has been shown to contain something once, so it may contain something every time after, and it
never has to appear again to keep collecting. That is §2.1's corollary — intent the world *seems*
to have — bought outright with a single sighting.

**Dread gate.** *Ambiguity* (§2.1) — **pass, and it is a WITHDRAWAL, not a deposit; the first this
ledger has recorded.** What it spends is the belief that the dark is empty, and the spend is
finite: every sighting after the first buys less. The accounting requirement that follows is
constraint (a) on the §10 row — **the all-clear must never arrive**, which is why the exit is
unwitnessed. A watcher that walks off while you watch has handed you proof it is gone, and the
device is over. *Clock* (§2.2) — **pass by inheritance only**, borrowed from the cycle, and see
the dwell-timer row above. *Break* (§3.1) — **FAIL, sixth consecutive entry — and the first time
the failure has changed shape.** Five entries failed this because *no threat existed*. One exists
now, and it still fails, because **a threat that cannot cost anything is not a consequence.** The
build (walking out for wood) breaks into a *recognition*, which breaks a belief but not a body;
nothing is lost, so nothing is at stake, so the arc has no floor. That is a smaller failure and a
different one, and it must not be reported as a pass. **What would fix it is a Talon open call and
not an engineering task** — canon lists *"what the creatures take when the fire goes out"* as
undecided, and the raid is the break this instrument is built to eventually deliver.
*Incompleteness* (§3.2) — **pass, and this is the best-behaved gate in the entry.** The sighting
resolves nothing whatsoever: no information is gained, the tension it raises is entirely left
standing, and the vanish denies even the small closure of having watched it go. *Degradation*
(§3.4) — **pass by inheritance, unbuilt in substance.** The lit radius shrinks night over night,
so the ground the watcher is forbidden shrinks with it and the exclusion zone degrades for free.
**The watcher itself must not escalate** — no per-night boldness, no closer approach, no longer
dwell — because a threat whose rules change between nights is §7.2's broken threat rather than a
mysterious one, and because that would be an entity-contract change this direction has no
authority to grant. *Affect* (§6.0) — **pass, conditional, and the conditions are the packet's own
falsifiable claim.** Two of them. **(i) It must be recognised as a body before it is recognised as
a creature** — if it resolves as an abstract pale shape or as a light, it is a prop, and the
failure mode to watch for is not "too subtle" but "reads as a texture." **(ii) It must be found,
not delivered** — nothing may direct the player's attention to it: no sound, no light change, no
camera move, no cue of any kind. The instant the game points at it, the game has told the player
it is there, and §8.2 has eaten the beat. *Fairness* (§7.1, §7.2) — §7.1 avoidable-but-barely is
**n/a today** (there is nothing to avoid), and saying so is more useful than a hollow pass. §7.2 is
the live one and it carries the entry's sharpest requirement: the watcher's rule is *it looks at
whoever is most visible*, and **that rule must be learnable by watching it happen.** If a player
cannot perceive attention transferring, the doe run is not a mechanic, it is a rumour — which is
the risk the pivot plan already names as the MVP's only genuinely hard part.

**The head turn is the readout, and the eyes are the readout's binary.** This is the entry's one
craft finding and it decides whether the second device row exists at all. The watcher has exactly
one output channel — where it is facing — so the turn must be the slowest and most legible thing
it does, and it must read from prone, from behind, at distance, with no UI, no sound and no
highlight. Two consequences that are requirements rather than tuning: **the head must break the
body's silhouette**, because a head sunk into the body mass rotates without changing the outline
and therefore says nothing at 40 m; and **the eyes carry the binary** — two dots present means it
is facing you, absent means it is not. That is a presence/absence signal legible at any range the
dots survive at all, it is the cheapest legibility instrument available, and it lands in
`LEVEL-BIBLE.md` §8.2's *honest* cue lane rather than §8.3's dread lane: the eyes tell the truth
about attention and never hint that something is about to happen. Which is also why they must stay
achromatic — see the colour-temperature row.

**Spike gate: no spike claimed, and one is refused — but the refusal costs something and the cost
is stated.** *Stake* — **FAIL**, nothing is on the line. *Violation* — **pass**, emphatically.
*Simultaneity* — **FAIL by construction and on purpose**; it faces one player and the asymmetry is
the point, which means the contagion §4.1.3 depends on has to travel by voice rather than by shared
perception. *Legibility* — **conditional**, on the two affect conditions above. Not a spike, and it
must not be staged as one: §4.4 caps the session at roughly one and §8.4 forbids the fixed
set-piece a staged sighting would become. **The honest cost, recorded because Talon should hear it
from this file and not from a disappointing clip:** this device's best moment is deliberately
*unwitnessed*, which makes it structurally un-clippable, and §4.2 predicts precisely what follows —
**the watcher will be much better to play than to watch.** That is the correct trade for a game
whose thesis is the walk rather than the encounter, and it is a trade, not a free win.

**Prohibitions, all nine.** §8.1 over-exposure — **the live prohibition, and the one this entire
pass exists to protect**; three inoculation findings are already on record (oversized interiors,
menu treeline, lake surface) and all three say the same thing about a surface that reliably
produces a wrongness. Four constraints follow: it must be **rare**, it must never fire at a fixed
point in the firewood fetch, it must never be **approached and studied** (see §10 constraint (c)),
and it must never be met **reliably** anywhere. §8.2 startle as build — **hard constraint, three
places**: no sting on arrival, no arrival inside the player's view, and no audio or camera
direction toward it, ever. The device is that it was already standing there. §8.3 reliable
telegraphs — **not violated, and three inherited flags are discharged here rather than deferred
again**: appear position may not correlate with canopy density (the canopy entry's flag), may not
correlate with terrain concavity (the land entry's flag), and the woods may not become where it is
reliably met (the campfire entry's flag). Plus one new guardrail the campfire entry's logic
demands: **the firelight must never flicker to hint at it** — that fuses the fairness lane and the
dread lane, which is the exact thing §8.3's parenthetical exists to prevent. §8.4 scripted
set-pieces — **not violated, live risk**: the variation must live in the possibility space (who is
standing, who is carrying, who ran), and nothing may be authored to fire at a fixed point or on a
fixed night. §8.5 illegible chaos — **the live risk, in its third form, and it is the same dial the
lake and the canopy each named**: unresolvable and illegible are one notch apart. Resolving as a
body and failing as a species is the target; a shape that resolves as neither is noise wearing a
creature's costume. §8.6 unrelenting tension — **pass, structurally**: it is rare, it does nothing,
and it leaves. §8.7 permanent safety — **FAIL, carried forward, sixth consecutive entry.** The camp
core is still permanently bright and the cabin still never degrades. **One contribution, recorded
and not claimed:** lit ground now has a *predator-defined* meaning rather than a lighting one,
which is the first time the safe radius has meant anything to anything but the renderer. That
sharpens the contrast without paying for it, and sharpening is not paying. §8.8 streamer-first —
**pass, structurally and unusually strongly**: the best moment is by design unwitnessed, so there
is nothing here shaped for a camera. §8.9 punitive failure — **n/a**, and it is n/a for the same
reason §3.1 fails: nothing can be lost. Forward constraint for whoever gives it teeth — the cost
must be **time or supplies, never progress**, the same constraint the land pass filed.

**One new fork added to §13**, proposed with its trigger and deliberately not surfaced. **No fork
resolved, and §6.2 is not moved.** §9's ratio is untouched: no absurd payoff is invented here and
none is implied — this beat is directed sincere, and the packet is flagged rather than resolved on
that axis exactly as §9 requires until the ripeness trigger fires.

**Handed off, unanswered — implementation questions this direction does not answer and must not.**
How rare is rare, how long a dwell, how far beyond the lit radius, how fast the head turns, how
bright the eyes, and how the appear position is chosen given the two correlation prohibitions
above — all values, all playtest calls, all the builder's. Whether the *proximity* withdraw
(§10 constraint (c)) belongs in the entity's behaviour contract as a fourth exit trigger is routed
to that contract per §0 and is not absorbed here: the direction states that studying it must be
impossible, and the contract owner decides by what mechanism. Whether the watcher and the M3 panic
drop ship as one feature or two is a scope call for whoever holds both.
**Camp night — direction-consistency defect fix (P1, 2026-08-08 playtest defect D7), `system`
altitude.** Not a new direction and not a beat: `OutdoorAtmosphere.cs`'s sky/ambient renderer
disagreed with itself by view angle — "looking beyond the trees... the view is perfect night...
but when the camera leaves that view it turns back into what looks like normal day again"
(Talon, verbatim). This entry exists because the *seeded* **Night reversal** row already
depends on this instrument working, and a broken instrument is worth recording against, not a
fresh spend.

**Why this is a repair, not a spend, stated in the row's own terms.** For the true-night span
(`CycleBands` night-start through dawn-gold-peak) the renderer now sources the sky/ambient from a
single direction-independent colour (mirroring the approved `a-moonlit` reference,
`MenuLook.Presets[0]`) instead of a directional gradient, with the crossover authored as an exact
colour match to the gradient's own value at that instant — provably invisible, not tuned to look
invisible. Verified headed: `docs/qa/2026-08-08-p1-night-direction/contact-sheet.png`, four
phases × four headings; the night rows read as uniform darkness at every heading.

| Device | Status | Note |
|---|---|---|
| Night reversal | **leaned on, not resolved — the instrument it depends on repaired** | §6.2 fork untouched: `NightAmbientFloor` / `NightMoonMax` (the darkness dial's ENERGY values) are byte-for-byte unchanged. Only the colour SOURCE changed, never the floor. Sixth consecutive camp entry with this posture. |
| Colour temperature as threat channel | **untouched** | The dusk sweep's warning grade (indices 0–4, the orange→plum→indigo arc) is not touched by this gate — the fix engages only from night-start onward, after the warning has already played. Still the sole warning lane. |
| Day/night cycle as clock | **borrowed, not re-spent** | No second clock; the fix rides the existing phase, gated on the same `CycleBands` breakpoints every other camp entry already reads. |
| Fog as sightline denial | **untouched** | `NightDome`'s fog/adjustment grade and its ownership seam with this component are unchanged; the moon disc was given `DisableFog` specifically so it does not interact with that channel at all. |

**The finding worth stating plainly.** A night that visibly contradicts itself by camera
direction does not read as ambiguous or dreadful — it reads as broken, which is *worse* than
inert for a device the 2026-07-26 playtest already logged as "creepy… extremely effective." §1's
ambiguity is supposed to come from ***the world***, not from the renderer arguing with itself.
This packet is defect remediation that restores the seeded device's credibility; it does not
manufacture a new feeling and does not move the needle on §6.2, §9's ratio, or anything else open
in §13.

**The cartoon moon is not a device.** Replacing the moon's automatic bright sky-disc glow
(`DirectionalLight3D.SkyMode = LightOnly`) with a fixed, hard-edged unlit disc is legibility
dressing on an existing celestial body, not a new dread/spike producer, and earns no §10 row.

**Dread gate.** Ambiguity — n/a, no fuel spent or added; this is instrument repair beneath an
existing device. Clock — n/a. Break/Incompleteness/Degradation — n/a, inherited unchanged from
every prior camp entry (no threat exists in the lean build; §3.1 still fails for the reason it
has failed in every camp-adjacent entry above). **Affect (§6.0) — the one live gate, and it flips
from fail to pass.** Before this fix, night was perceived and felt *wrong* (a rendering
contradiction, not a directed sensation) — arguably a sharper §6.0 defect than "perceived and
feels like nothing," because a self-contradicting effect reads as a bug and teaches the player
to distrust the whole night system. After: the reversal reads as itself, consistently, from
every direction. Fairness — n/a.

**Spike gate: no spike claimed.** A lighting-consistency fix is not a beat.

**Prohibitions.** §8.2 startle as build — **the live constraint, honoured by construction**: the
architecture swap is keyed to colour-matched crossover points specifically so the switch cannot
register as a snap or a flicker. The other eight are n/a — nothing is shown, telegraphed,
scripted, chaotic, tension-adding, safety-changing, streamer-baited or punitive here; this pass
touches none of those axes.

**Canon updated:** this entry (new). No device row added or changed in §10. No fork opened,
closed, or amended in §13 — §6.2 stays exactly as the lake entry (2026-08-08) left it, "still one
side-by-side session, not an argument."

**Handed off:** nothing outstanding. The one open question is experiential, not implementational —
whether the now-consistent night reads as intended in a live session — and that is Talon's
weekend-test call, not a build question for any skill.

---

**The blind-night audio substrate — directed 2026-08-09 (packet 1f, Issue #182). Altitude:
system.**

Directed at `system` rather than `scene` because the input is a channel, not a moment: 1f builds
the looping-emitter path and the night bed's lanes, and manufactures no beat at all. Directing it
at `scene` would have invented one, which is exactly the failure the packet's own brief forbids.

**The feeling this substrate must manufacture, and where.** In the woods at night, away from
every light: *I know exactly where everything is and I still do not know whether to go there.*
Not disorientation — the darkness law would make disorientation trivially available and it is the
wrong feeling, because a player who cannot locate anything stops having decisions and §1's
anxiety column is explicit that a directionless state "has no shape." The substrate's job is to
keep the player **located and undecided**, which is §2.1's ambiguity with the navigation left
intact. The new §10 row *Position without judgment* is that requirement written as a rule.

**The three questions the packet asked, answered.**

*(a) What a fire's audible presence must mean.* It is **the map, and nothing else**. Its body
says *a fire, that bearing, that far* — and it must never be made to say anything about safety,
occupancy or threat, because the moment it does it has become the lane §8.3 forbids. This is also
the answer to the nearest-K rule's affect: the rank cutoff is not "the far fires stopped
mattering," it is **the horizon of what you can navigate by**, which is the correct and honest
reading of a resource the players built. Note what falls out of the darkness law and was not
obvious before it: *Refuge maintained only by leaving it* required the fire to be "legible from
inside the woods," and §1.5 deletes the sightline that requirement assumed — so the fire's
continuous body is no longer the audio *half* of that legibility, it is all of it. That row is
amended accordingly.

*(b) What the night bed must and must not do.* It must be **continuous, low, and eventless**, and
the *unreadable dark* row's weather/events carve-out now governs its content verbatim (see that
row's third host). Positively: it must **leave a hole where a creature's voice will go** — the
bed is shaped so a threat is audible *through* it without the bed having to move. That is the
single most important constraint in the packet and it is a §8.3 finding, not a mix preference:
**a bed that ducks for a creature has announced the creature.** §7.3's "audible presence before
visible presence" is therefore a property of the bed's *spectrum*, not of anything the bed does
when a threat arrives — the bed must do nothing when a threat arrives, ever. Night reads as
**less** (§6.1, already directed and shipped in `AmbientBed`'s night gain); that is cited, not
re-decided.

*(c) Whether this collides with `LEVEL-BIBLE.md` §8.1 and §8.3.* **Neither, and the reasons are
different.** §8.1 (audio alone is never a sufficient cue) is satisfied without special pleading
because plan §15 already aligns the audible fire set with the impostor-glow set at the same rank
cutoff: a fire you can hear is a fire whose glow you can see, so the cue is genuinely redundant
across two channels and the redundancy carries *the same ambiguity* on both — where and how far,
never whether. §8.3 (no cue that always fires) does not bite because the fire's body is an
**urgency/fairness** cue, and §8.3's own parenthetical exempts those and requires them to stay
honest. What §8.3 *does* impose is constraint (a) of the new row: the fairness lane and the dread
lane must not share a bus or a modulation source. That is a build requirement and it is met by
routing the fire's body to the clean scenery lane rather than to the ducked bed lane.

**Devices.** *Wrong silence* — **deposit, NOT spent**, and enlarged (see the §10 row, including
the new cull-must-fade refusal, which is the fifth time this ledger has had to stop `Withdraw`
being wired to something with an obvious cause, and the first time the tempting caller was an
optimisation rather than a game event). *The unreadable dark* — **third host, deposit, NOT
spent**. *Per-player sensory asymmetry* — **first carrier built**, no beat authored on it.
*Sound with no visible source* — **precondition built, deliberately not fired**. *Refuge
maintained only by leaving it* — its night-side legibility carrier built; payload still #153's.
*Position without judgment* — **new row**. *The watched* — **untouched, and protected**: nothing
in this packet lets the bed, the fire or any lane react to the watcher, which is §12's R3 refusal
honoured by construction rather than by care.

**Dread gate.** Ambiguity (§2.1) — **pass, as an investment**: the packet spends no fuel and
builds the channel two devices were waiting on. Clock (§2.2) — **n/a, deliberately**; a substrate
is not a clock and authoring one here would be the second clock §13 already has a fork about.
Break (§3.1) — **fail, and correctly**: a deposit has no break, and a substrate that manufactured
a break would be a beat nobody directed. Incompleteness (§3.2) — n/a, nothing is released.
Degradation (§3.4) — **the live gate, and it is why the new §10 row exists**: an information
channel that answers everything is a sanctuary that never degrades (§8.7), so the *where, never
whether* rule is what keeps the darkness law from defusing itself. Affect (§6.0) — **pass on the
fire, deferred on the bed**: the fire heard from inside the woods is a directed sensation
(relief with a distance attached); whether the bed *feels* like anything cannot be known without
ears and is honestly reported as unverified. Fairness (§7.1, §7.2) — **pass, conditional on the
cull fading**: a fire that cuts out at a rank boundary is a rule that changes without a cause the
player can learn, which is §7.2's exact failure.

**Spike gate: no spike claimed.** A substrate is not a beat and 1f authors none.

**Prohibitions.** §8.1 over-exposure — honoured; nothing is shown or voiced, and the packet
explicitly declines to author any creature's voice. §8.2 startle as build — honoured by
construction; no transient is authored anywhere in the night mix and the cull fades. §8.3
reliable telegraphs — **the live one**, and it is answered by the lane separation above rather
than waved at. §8.4 scripted set-pieces — honoured; the bed is generative and eventless, and the
fire's audibility falls out of where the players built fires. §8.5 illegible chaos — honoured;
the reserved vocal band is the mechanism that stops the night becoming noise with no discernible
cause. §8.6 unrelenting tension — honoured; the bed adds no tension to be unrelenting with, which
is the point of a deposit. §8.7 permanent safety — **the second live one**, see the degradation
gate; a channel that always answers *whether* would be the game's first sanctuary made of
information. §8.8 streamer-first — n/a. §8.9 punitive failure — n/a.

**§6.2 was not resolved and was not leaned on.** `MinAmbientEnergy` is untouched, no mix decision
here implies a floor value, and the drafts pending in PR #187 are unaffected. The one thing this
entry contributes to that fork is worth recording because it is new: an audible night is the
first thing that makes side **(b)** — drop the floor, let the fire carry navigability — cost
*less* than it did, because the fire is now findable without seeing it. That is an input to the
side-by-side session, not a vote, and the director still does not pick.

**Canon updated:** this entry (new); one new §10 row (*Position without judgment*); five §10 rows
amended (*Wrong silence*, *Per-player sensory asymmetry*, *Sound with no visible source*, *The
unreadable dark*, *Refuge maintained only by leaving it*); one new §13 fork (audible reach vs lit
reach).

**Handed off, unanswered:** how far the fire's body carries and how long the cull fade takes
(`sound-spatial-audio`, then playtest); the reserved band's exact edges (`sound-soundscape-construction`,
and they will want revisiting when the first real creature voice exists); whether the bed should
ever duck under a *threat* rather than under voice (refused here as a §8.3 telegraph — if anyone
wants it, it is `vfx-audio-sync`'s to ration, not a mix call); and every creature's fear
signature, which stays `/direct`'s to author at each creature's own packet before
`sound-threat-audio` builds it.

**Camp glow stick — directed 2026-08-11, stamped by Talon the same session, and BUILT the same
session.** Altitude: **system** — a light with a lifetime, at any place a player chooses to put it.
Not `scene` (no fixed position) and not `level` (no geometry of its own). A second pass at `level`
altitude is flagged for the first level that actually hosts a laid trail.

The direction in one line: **bright near, ghosts far, acid neon yellow, dead steady, and never
countable from camp.** Talon chose the near/far shape from three options and overruled the pass's
colour, and that override is recorded below because it is better than what it replaced.

| Device | Status in this system | Note |
|---|---|---|
| Canopy as spatial darkness / Landform as disorientation | **counterplayed, NOT defused** | New status word, and it is this entry's crux. The camp-woods passes took the sightline home *and* dead reckoning and left one compass (causal drainage) that points at the lake. The glow stick is the first answer the player can buy to "how do I get back". **Counterplay makes a device FAIR (§7.1); it does not spend it** — and it stays counterplay only because it costs money, a hand action, and burns out. The moment a stick is free, permanent, or visible from camp, this flips to `defused` and both woods devices go with it. |
| Route loss behind the group | **first honest host — NOT claimed** | `unbuilt` since 2026-07-25. A trail that burns out, or that something tears down, is route loss the player can *watch happen*. Recorded so the packet that implements it knows the ground is already here. |
| The unreadable dark | **not defused, on one condition** | A stick lights *itself* and its own patch of floor. The real-light pool is 4 m at ≤ 0.5 intensity and there are never more than four of them, so it cannot resolve the woods into readable geometry. A future pass that widens the lit radius spends the lake's deposit sideways. |
| Flash beacon | **kept whole** | The stick is placed and persistent, so it is a *waypoint*, not an ambiguous event, and the two must not share a visual language. Enforced: the flash is photographically neutral and brief; the stick is acid yellow and dead steady. |
| Cost-to-know instrument (§2.3) | **untouched — and the glow stick inverts its archetype** | §2.3's own example is *"a light that reveals and attracts."* In this game light **repels** (canon fact 2), so the archetype does not transfer and the row stays whole. First time canon has contradicted a Part I example; worth the ink so the next director does not reach for that row here. |
| Colour temperature as threat channel | **borrowed, and this pass is where it was most at risk** | See the colour finding below. The stick is a point object, not a grade, so the dusk sweep's orange→plum→indigo lane stays sole. |
| Night reversal | **leaned on, not resolved** — sixth consecutive camp entry | The stick adds a *portable, purchasable* navigability source, which strengthens sides (b) and (c) of §13's fork. It picks none of them and `MinAmbientEnergy` is untouched. |
| Per-player sensory asymmetry | **produced, not spent** | The player out on the trail perceives it; camp sees a smudge stop moving, or nothing. Falls out free; no `vfx-proximity` machinery built. Same posture as #152 and W5. |
| Wrong silence · Refuge maintained only by leaving it · Doorway · Stairs that lie · Accumulating wrongnesses · Forced separation · Open sanctuary · Fog · Scale · Rising tide · A world that breathes · Persistent scarring · Tension-budget pullback · Non-predictive threshold · Synced audio-visual peak · Teammate voice cut-out | **untouched** | Nothing here touches, spends, leans on or defuses any of them. |

**No new device row, deliberately.** The lake pass refused one on the same discipline and was right:
a light that runs out is `Route loss` and §3.4 finding a host, not a new technique. Inventing a row
for what a row covers is register inflation.

**The five:** *belief* — "I can find my way back"; *break* — you turn around and the stick you were
counting on has gone out, or you reach one and there is no next one, and the trail you bought does
not reach as far as you went; *why here* — it must break **on the return, in the woods**, never on
the way out: outbound you are spending and confident, and the walk home is when the purchase gets
audited, which is also why it cannot move earlier (you have to have laid enough of them to believe
in them first); *who perceives* — whoever walked out, and deliberately **not** everyone, which is
§5.4 asymmetry for free; *what changes* — the group learns that light they bought runs out on a
schedule they did not set, and that is the belief the vending economy and the string lights both
need installed before either can charge for reach.

**The colour finding, and it is Talon's correction to the pass rather than the pass's own work.**
The direction pass reached for a COOL light on §6.6's reasoning: warm reads as life and intimacy,
and the campfire owns that channel. Talon overruled it — *"unearthly colored like too yellow
neon"* — and the override is the better call, so it is recorded as the rule rather than as an
exception to one:

> **The discriminator is not temperature. It is natural vs. chemical.** A cool light borrows
> moonlight's vocabulary and reads as ordinary. An acid yellow reads as manufactured, which is
> precisely the §7.2 teaching job this object has: **this light does not protect you, and the fire
> does.** The separation from firelight is therefore carried entirely by CHROMA and STEADINESS,
> never by hue — desaturate it and it becomes a weak fire, which is the single most likely way to
> get this wrong.

**Dread gate.** *Ambiguity* (§2.1) — **pass, conditional** on the far field never resolving into
countable points; that one dial is the whole direction and it is now an asserted test plus a
photograph. *Clock* (§2.2) — **pass by inheritance only**, and stated in these words because it is
the third time: **the burn-down is not a clock.** §2.2 requires visible, shared, monotonic *and
inevitable*; a stick's burn is per-player, invisible from camp and self-initiated. It is §3.4
degradation wearing a clock's clothes — the identical finding #152 recorded about fuel and W5 about
chill. *Break* (§3.1) — **pass**, only the second real break in this ledger after #152's refused
match strike, and unlike that one it is **deferred**: you pay for the mistake an hour after you
make it. *Incompleteness* (§3.2) — **pass**; reaching a stick relieves this step and leaves the walk
standing. *Degradation* (§3.4) — **PASS, and it is this ledger's first non-structural instance.**
Every prior entry scored this "structurally, unbuilt in substance" (canopy openings, the longer
return, the cold ramp) or outright FAIL (#152). A stick that burns out is the first safety in this
game that actually runs out, and the first the player *bought*. *Affect* (§6.0) — **pass,
conditional** on the dying read being perceivable at trail distance rather than only up close —
#152's condition, carried over verbatim. Photographed: shot 05. *Fairness* (§7.1, §7.2) — **pass**,
with the colour finding above as its floor, because the §7.2 risk here is created by canon itself
(fact 2 says light is safety; small lights grant no ward) and must be taught by the object rather
than discovered by dying.

**Spike gate: no spike claimed.** A trail is not a beat and directing one as though it were is
§4.4 inflation. **But it is unusually good spike substrate**, same finding #152 made about the wood
fetch: a group with somebody out on a dying trail has manufactured stake and agency for free.

**Prohibitions, all nine.** §8.1 over-exposure — n/a, nothing is shown. §8.2 startle as build —
**live constraint**: a stick never goes out with a snap or a sting, and none may ever be
extinguished by an unseen cause as a scare; the distance falloff is smoothstepped for the same
reason, so no stick pops into existence at a fixed radius. §8.3 reliable telegraphs — **the live
risk**: the burn-down is a §8.2-lane *fairness* cue and must stay honest, so **the stick may never
be made to flicker to hint at a creature** — identical guardrail to #152's firelight, and the
shader carries no time term at all so that the violation would have to be deliberate. §8.4 scripted
set-pieces — not violated; a player-laid trail is the purest possibility space in the repo. §8.5
illegible chaos — **the live risk, fourth occurrence** (canopy, land, lake, now this): the far field
is one dial from noise. §8.6 unrelenting tension — pass; each stick reached is a micro-valley. §8.7
permanent safety — **PASS, and it is this ledger's first.** Six consecutive entries have failed it.
A glow stick is the first light in the game that is temporary by construction. §8.8 streamer-first —
pass, with a named temptation: a long bright trail is the most screenshot-able object in this
feature, and the necklace was the store-page answer. §8.9 punitive failure — pass, **forward
constraint**: a dead trail must cost **time**, never progress.

**What the build actually found, recorded because the ledger is worth more with it than without.**
The direction survived contact; two of its numbers did not, and both were caught by the headed
capture rather than by the reasoning:

1. **Geometric tangency is not perceptual merging.** The first two attempts sized the halo so
   neighbours would *touch* (radius ≥ half the spacing). Tangent halos contribute **nothing** at the
   midpoint between them — the falloff is zero at the rim by construction — so the capture
   photographed fourteen separate beads while the unit test asserting tangency passed. The real
   condition is radius ≥ **spacing**: each halo must reach its neighbour's *centre*.
2. **A bigger halo around a hot core is still a countable point.** Growing the radius alone did not
   merge anything. The core had to be faded out with distance, so the object's *character* changes
   across the near/far band and not merely its size. That is what "bright near, ghosts far"
   actually means in a shader, and the first two passes read it as a size instruction.

Both are now asserted tests, so re-breaking either goes red rather than shipping.

**One thing the capture found that belongs to the LEVEL, not to this system, and is not resolved
here:** from the campfire, a trail laid up the T1 corridor is **not visible at all** — the lodge and
the treeline occlude it completely (shots 03, 04). At this trailhead the "never countable from camp"
constraint is enforced by geometry before the renderer is consulted. That is the camp-woods pass
working exactly as directed and is recorded as a fact about the current map, not as a guarantee: a
future trailhead with a clear back-bearing would put the whole weight back on the angular floor.

**One flagged, not fixed:** shot 07 puts two sticks beside the campfire's own shipped light and the
sticks read *louder*. The comparison is unfair — the stand-in is light-only, with none of the real
fire's flame geometry or crackle — so this is a **flag for the first composite playtest**, not a
finding, and canon fact 3's numeric backstops are asserted separately and hold.

**One new fork added to §13** (proposed with its trigger, deliberately NOT surfaced to Talon —
nothing can be tested because no creature exists).

**Amended 2026-08-13 — the sound, retroactively directed.** Commit `3427960` gave the stick a
placement crack without a pass (PR #233 flagged the debt); the code was read on the promotion
tree and the mechanical contract holds: once per peer per genuine placement, positional at the
stick's server-resolved position, silent on late-join, and **burnout stays silent** — the §8.2
constraint above ("a stick never goes out with a snap or a sting"), honoured in code. No new
device row. The direction, recorded so the ledger stops being wrong about what exists:

- **The crack is an honest-lane fact cue — *a teammate just placed a stick, that bearing* — and
  it must stay honest.** It sits inside *Position without judgment* exactly: it may say a stick
  went down, where; it may never say its placer is safe, or anything else. Never synthesized
  falsely — no fake cracks, ever. And a forward guardrail: **if a mimic creature is ever
  authored, the crack is the one sound it may not counterfeit without surfacing a fork** — a
  counterfeit fuses the fairness lane and the dread lane §8.3 exists to keep apart.
- **It is the game's first manufactured sound vocabulary** — a chemical snap, unmistakably a
  tool — which buys in audio the same discriminator the colour finding bought in light: natural
  vs. chemical. The first creature voice will be organic, and the contrast comes free, already
  taught.
- **The §5.4 row above was scored on light alone; the crack extends it through occlusion.** A
  player who cannot see the trail can hear it being extended — the first purely audible
  teammate-activity read in the game, carrying past sightline in exactly the woods where
  sightline is what the canopy spent. Produced, still not spent; no machinery built.

THRILL §5.4 / §8.2 / §8.3 are now checked for the sound; the debt PR #233 flagged is closed.
Handed off, unanswered: mix balance of crack and dud under the night bed (headed, weekend feel
notes); the denied-drop cue's missing visual channel is INTERACTION §8.4's blank, queued for
Talon post-weekend, and is not this file's to resolve.

**The fog savanna — directed 2026-08-13, pre-build (the first anomaly pocket, per
`docs/WORLD-ANOMALY-POCKETS.md` and its lean-law addendum; Talon approved the direction and this
pocket's build order the same day).** Altitude: **level** — a bounded zone with an approach, an
interior, and an exit. A `system` pass on the fog layer itself is flagged for when it is built.
Register, per Talon's 2026-08-13 ruling: **wonder and dread together, absurd on top** — and the
plain note that *wonder* is a register Part I does not theorise; it is recorded as Talon's word,
not derived, and no doctrine section is edited on its strength.

The direction in one line: **the deepest woods open onto a place with too much sky and no
ground — sparse giant trees of one silhouette, above a still fog you wade through blind.**
It gives you the sky and takes the ground: prospect restored in the one direction that never
helps (§6.1, upward), denied where it matters (level). At night the crowns are the map's most
legible silhouettes-against-sky — canon #4's vision, granted — while the fog below is the
blindest ground on the map. By day the same place is absurd, open wonder. One geometry, re-signed
by the clock, which is the register ruling delivered by construction rather than by tuning.

| Device | Status in this level | Note |
|---|---|---|
| The bounded exception | `spent-here` — **its first spend** | The pocket's whole engine. One law: sparse giants over fog. Nothing else strange is allowed in frame, and no second pocket may share this law. |
| Canopy as spatial darkness | **inverted at the boundary, not violated** | The pocket authors no second position-keyed darkness system — it is the *removal* of the first, in a room with an edge. The sharp boundary is what keeps "darker means deeper" honest as a woods rule (§7.2): the pocket is visibly not-the-woods, so the exception never teaches that the rule lies. |
| Fog as sightline denial | **not spent — mechanism only** | That row's reuse rule is explicit: only where something approaches. Nothing approaches here; the fog is still. Same posture as the canopy and lake entries. |
| Scale that is not the player's | **leaned on, not re-spent** | The giants inherit the woods' scale grammar (trunks wider than a player, the human band empty). The pocket's own law is openness + one silhouette, not size — scale keeps the world coherent across the threshold, it does not carry the strangeness. |
| Landform as disorientation | **not spent, and one hard floor** | The fog hides the ground, so the land pass's requirement 2 binds absolutely here: **the savanna floor is gentle — no drops, no snags — because it may never cost footing it has made invisible.** |
| The unreadable dark | **night posture only — deposit discipline, NOT spent** | The fog inherits the weather/events carve-out verbatim (fourth surface). Slow drift is weather; a wake, a parting line, a shape, an uncaused stir is an event and is refused outright. |
| Accumulating wrongnesses | **NOT hosted, deliberately** | The pocket thesis and the lean law agree: one law, nothing else in frame. The savanna is not where a wrongness lives; it *is* one, once, at landscape scale. |
| Wrong silence | **REFUSED — sixth occurrence** | Do not wire `AmbientBed.Withdraw()` to the pocket boundary. Crossing a treeline into open ground is the most caused transition in the game; a cause defuses §6.3. The savanna's bed is *different weather* (open wind lane in place of woods rustle — `sound-soundscape-construction`'s build), never an absence. |
| Colour temperature as threat channel | **untouched — fifth occurrence of the same trap** | Night fog wants to go cool blue, and cool is the warning direction. The fog reads paler and lower-contrast at night, never bluer; the dusk sweep's grade stays the sole warning lane. |
| Night reversal / Day-night cycle as clock | **borrowed, not re-spent** | The pocket authors no clock and no darkness of its own; the cycle re-signs it like everything else. |
| Glow-stick trail (counterplay rows) | **first level-altitude host — the flag the glow-stick entry left** | Fog amplifies "bright near, ghosts far" for free: halos diffuse, cores die with distance, and the trail through the fog is the pockets doc's own fantasy image. The lean law forbids staging it — players compose that image or nobody does. |
| Per-player sensory asymmetry | **produced, not spent** | The finder narrates a place that sounds invented; camp hears the voice, not the place. Falls out free, no machinery built. |
| The watched / any creature | **protected, not placed** | Nothing here grants placement or capability. Whether any creature may enter a pocket is its own contract's question, and the §8.3 flag below applies to whoever answers it. |

**The five.** *Belief* — deeper means darker, denser, more woods; the woods are the world, and
five directed passes have taught exactly that. *Break* — the deepest treeline opens: sky where
sky has been impossible for two hundred metres, and no ground where ground was never in
question. *Why here* — it must sit deep, because necessity (canon #5's deepening wood range)
is what walks players into it — a pocket found by tourism is a postcard; found on a wood run
it is a discovery under pressure, and the approach through maximum enclosure is the setup the
reveal spends. Move it shallow and it is scenery. *Who perceives* — whoever walks in, usually
not everyone; the retelling travels by proximity voice, which is the contagion §5.1 names.
*What changes* — the world stops being one substance. The map acquires a destination the
players will name themselves (the game never names it — lean law); glow trails acquire
somewhere to go; and "the world is more than it seems" becomes a fact the group owns rather
than a promise the game made.

**Constitutive constraints (requirements, not tuning).**

1. **The edge is a door, both ways.** From inside, the woods' dark mass must read *over* the
   fog from anywhere in the pocket — the fog denies the ground, never the horizon. That is the
   exit compass and §7.1's floor: a pocket that can strand a player who can no longer find the
   treeline has converted wonder into a trap nobody chose.
2. **The floor is gentle** (the Landform row above). The fog may cost time and nerve, never
   footing.
3. **The fog is gameplay, therefore server data.** If eye height is inside it, concealment is
   real, and the parity law says real concealment is the server's fact, not the renderer's.
   Routed to `MECHANICS-BIBLE.md` / `/spec-level`; stated here because a presentation-only fog
   that hides gameplay-visible things is the parity violation the canon law exists to prevent.
4. **Nothing is ever in the fog.** No events, no shapes, no disturbances (the Unreadable-dark
   row above; §8.1 below). The pocket's charge under the lean law is that ONE thing is strange
   and nothing else happens, ever, no matter how long anyone stares.
5. **One silhouette.** The trees are one species-shape repeated at rolled scales. Variety is
   the enemy of the law — and the fog hides repetition, the doc's own cost note, so this
   constraint is free.

**Dread gate.** *Ambiguity* (§2.1) — **pass, by construction**: the lean law *is* §2.1 written
as a build rule; the place explains nothing, answers nothing, and never resolves into a reason.
What it spends: one violation of the world-model, on first entry, per group. Revisits settle
into a wonder-room, which is the intended register (see the §10 row). *Clock* (§2.2) — **pass
by inheritance only**; the savanna authors none, and alone it is a place, not dread — the dread
arrives with the night, the wood quota, and the walk home. *Break* (§3.1) — **positional pass,
threat-break FAIL carried forward** (seventh consecutive camp-adjacent entry): stepping through
the treeline is a real level-altitude break in the canopy entry's sense, and nothing in the
build can yet make being here *cost* anything. *Incompleteness* (§3.2) — **pass**: the reveal
answers "what is out here" with a fact and no meaning; the larger question stands. *Degradation*
(§3.4) — **n/a**: the pocket is not a sanctuary (no cover, no warmth, no refuge) and so neither
degrades nor worsens §8.7. *Affect* (§6.0) — **pass, conditional**: wading the fog must feel
different from walking the woods — through movement, sightline, and sound, not through a
palette. If the honest answer after a playtest is "it is more open," that is the treeline
defect again, in the most important new place. *Fairness* (§7.1, §7.2) — **pass, with
constraints 1 and 2 as floors**, and one more: the pocket exists at a rolled position per run,
which is variety about the *map*, never about the *rules* — the same law rolled elsewhere must
behave identically.

**Spike gate: no spike claimed.** A place is not a beat (the canopy and land precedent, third
time). Substrate noted, not claimed: a group following a dying glow trail into fog at night has
stake, agency and simultaneity manufactured for free, and whatever eventually threatens the
walk should be pointed at windows like that one rather than at a fresh one.

**Prohibitions, all nine.** §8.1 over-exposure — **hard refusal**: nothing is ever in the fog,
shown above the fog, or silhouetted between the crowns — not once, not rarely, not as an easter
egg (inoculation, fourth surface). §8.2 startle as build — **live constraint**: the threshold
is spatial, not an event — no sting, no audio cue, no light snap on entry; the door is walked
through, not fired. §8.3 reliable telegraphs — **forward flag, fourth of its kind** (hollows,
shoreline, canopy density, now pockets): the first packet that reliably places evidence,
spawns, or creatures in pockets converts the world's wonder rooms into telegraphs, and the
packet that breaks this will not be this one. §8.4 scripted set-pieces — **not violated**: the
pocket fires no events; per-run macro placement keeps discovery emergent, and nothing may be
authored to happen on first entry. §8.5 illegible chaos — **pass in the interior** (one law
makes it the least chaotic place on the map); **the risk lives at the boundary**: a blended
edge is mush, which is the doc's own thesis and constraint 1's other half. §8.6 unrelenting
tension — healthy; the day savanna is a genuine valley, and that is allowed. §8.7 permanent
safety — **not worsened, for the first time in seven entries**: the savanna offers no refuge to
be permanent. §8.8 streamer-first — **the named temptation, sharpest in the ledger**: this is
the game's postcard, the doc calls the glow trail through it "the fantasy image of the game,"
and the lean law is the guard — no authored vista, no camera move, no staged reveal; the
players compose the image or nobody does. §8.9 punitive failure — n/a today; forward
constraint: being lost in the fog costs **time, never progress**.

**Canon updated:** this entry (new); one new §10 row (*The bounded exception*), with the fog
savanna as its first spend; the glow-stick entry amended the same day (the sound). No fork
opened, none resolved: the pockets doc's forks 1 and 3 stay frozen per the 2026-08-13 plan,
§6.2 is untouched, and §9's ratio is untouched — no absurd payoff is invented here; the day
register carries the absurd side on its own.

**Handed off, unanswered — implementation, not direction:** fog depth, band height, tree count,
pocket size and placement depth (macro-pass values; the builder's, then playtest); how the
server visibility model represents the fog band (`MECHANICS-BIBLE.md` / `/spec-level` — a
parity question, constraint 3); whether the savanna holds gatherables so that necessity, not
curiosity, routes players through it — a `/spec-level` composition call, and canon #5 is the
reason the question matters; the fog's render technique and its cost on the perf floor
(`vfx-lighting` / `vfx-particles`); the open-wind bed lane (`sound-soundscape-construction`);
and the boundary's exact read at night under the darkness law (`vfx-lighting`, with constraint
1 as its acceptance test).

**The electric eel kit** *(RETIRED — prey-animal species identity and species-locked panic mechanics were retired 2026-08-22, canon §3. Kept as the dated record of a 2026-08-13 direction; do not route new work from it.)* **— directed 2026-08-13 (packet STORMVAULT-EEL-2), against the finished
mechanism contract `docs/design/2026-08-13-electric-eel-prey-kit.md`.** Altitude: **system** — a
resource, a verb, and a propagation rule, fired many times a night at no authored position.
Not `scene`: directing it as a placed beat would be §8.4's scripted set-piece in advance, the
same call the panic drop (R4) and the glow stick each made. A `level` pass is flagged for the
first level that actually hosts a six-eel party. **Two feelings were directed and only two** —
holding a full charge, and a chain going off. Register, per Talon's 2026-08-13 ruling: wonder
and dread together, **absurd on top, never instead of**; here the slapstick sits on top and
where the dread underneath it actually comes from is the §3.1 finding below, which is this
pass's headline.

**The mechanism is not this file's and was not touched.** Charge accrues passively and never
decays; a release fires above a floor and scales linearly; the eel launches itself on a
**random** bearing; a blast chains every charged eel in the same circle, each releasing at
most once. Nothing below adds reach, duration, damage, a creature reaction, or a channel. The
kit's effect on creatures is an explicitly empty stub (canon 7) and **no feeling directed here
presumes a creature perceives, hears, flees or reacts to a blast** — that inference is the
single most likely way this direction would grant a capability, and it is refused by name.

| Device | Status in this system | Note |
|---|---|---|
| **Contagious panic** | **new row — directed, instrument not built** | The kit's whole engine, and the reason it earns a row rather than borrowing three. Not `spent-here` until a party has actually been emptied by somebody else's twitch in a played session — the hoodlab mistake this ledger already records twice. |
| Forced separation | **first genuinely FORCED host — still NOT spent** | See the amended §10 row. Five entries have now written *invited, not forced*; this is the first that takes the choice away, and §5.3's precondition (togetherness worth something) is still unmet, so it remains unpaid-for mechanism rather than a spend. |
| Synced audio-visual peak | **kept whole — third refusal, same reasoning both prior times** | A blast aligns audio and visual in one instant for every player, which is §4.6's exact shape and the most tempting log in this entry. Deliberately not spent: it is a **release**, it repeats many times a night, and §4.6 is reserved for a spike. Aligning a release across channels is craft, not a spend — identical to the campfire's ignition and the lake's go-under. |
| Wrong silence | **REFUSED — seventh occurrence, and the most seductive caller yet** | **Do not wire `AmbientBed.Withdraw()` to a blast, to the ragdoll, to the landing, or to the beat after a chain.** §6.3 needs an absence with *no explanation*; a bang is the most complete explanation an absence has ever had, and "the world rings and then goes quiet" is a real-world cause everyone in the room already knows. Worse than the six prior temptations in one specific way: the others were state changes and this one is a **transient**, so a builder reaching for it would be reaching for a stock post-explosion effect rather than for a device — which is exactly how the strongest row in this file gets burned by somebody who never read it. If the blast changes the mix at all it is a **filter, never a gain**, the same ruling the lake and the panic drop each received. |
| Position without judgment | **obeyed, not spent** | The blast's crack is an honest-lane fact cue — *an eel went off, that bearing, that far* — and it sits inside this row exactly as the glow stick's placement crack does. It may never be made to say *and it is safe now*, *and something is coming*, or anything about occupancy. **Forward guardrail, extending the glow-stick amendment: if a mimic creature is ever authored, the blast is the second sound it may not counterfeit without surfacing a fork** — a counterfeit fuses the two lanes §8.3 exists to keep apart. |
| Flash beacon | **kept whole, one guardrail** | A blast is a brief bright event at a position, which is #108's reserved shape, and at distance in the dark the two could collide. They do not, on one condition: **the blast is never perceivable as a bare, meaningless light from further away than the bodies going up are.** The device #108 reserves is an *ambiguous* pulse; a blast whose consequence arrives with it is unambiguous and spends nothing. A blast visible as a lone far-off flicker with no read is #108 spent sideways, and it is also §8.1 redundancy failing. |
| Colour temperature as threat channel | **borrowed, untouched — sixth occurrence of the same trap, and this is the first non-surface instance** | The land pass flagged the damp hollow, the lake the night water, the watcher the eyes, the savanna the fog — all of them slow. This is a **transient**, and electricity wants to be blue-white, which is the warning direction. **Requirements: the blast is an event, not a grade — it may not tint the world, and it must never read as firelight.** The discriminator the glow stick landed (natural vs. chemical, carried by chroma and steadiness, never by hue) is already taught and this object inherits it for free: a discharge is manufactured, a fire is not. The dusk sweep's orange→plum→indigo arc stays the sole warning lane. |
| Night reversal | **leaned on, not resolved — seventh consecutive camp entry with this posture** | The dark gets one new cost here, and it is a different one again from the lake's (you cannot see the medium you are in) and the watcher's (the dark is where the thing may stand): **the dark is where you land when you did not choose.** No threat capability is required to deliver it. `NightAmbientFloor` / `NightMoonMax` are untouched, nothing in this direction depends on the floor's value, and §6.2 is not moved one inch. *Noted, not resolved:* a sibling session established 2026-08-13 that §6.2's live-conflict box names `DayNightSky.MinAmbientEnergy`, a constant used only by lab scenes, while the camp's real floor is `OutdoorAtmosphere.NightAmbientFloor`. That is a naming defect in the box, not a change to the fork; nothing here is built on either constant and the fork stays exactly where the lake entry left it. |
| The bounded exception | **REFUSED — its first spend is reserved and stays reserved** | The fog savanna owns it (2026-08-13). The eel does not get it, does not borrow it, and nothing about a blast may be staged as a world-rule violation. |
| The unreadable dark | **not defused, on one condition** | The night bed's weather/events carve-out governs the *world's* content; a blast is unmistakably player-caused and legible, so it is neither weather nor an uncaused event and does not draw on any of the four deposits. The condition is the Flash-beacon guardrail above: a blast that reads at distance as *something happened out there* with no visible cause has stopped being a player verb and become an event on an inert surface. |
| Attention as a resource the group can move / The watched / Cost-to-know instrument / The blind hide | **untouched, and explicitly NOT drawn on** | Every one of them would require a creature to perceive the blast. Canon 7 makes that Talon's ruling and §9.1's table is empty; inferring it through affect is still inferring it. Named individually so the refusal reads as a check rather than an omission. |
| Refuge maintained only by leaving it / The ration that never refills | **untouched, and worth one note** | The blast cannot touch fires, hearths or strung lights (mechanism §5.2). This is the first player verb in the repo that could plausibly have threatened the banked score and does not, and the two devices that live on lit ground are unaffected by it. |
| Accumulating wrongnesses | **untouched, and explicitly NOT hosted** | Nothing may be authored to happen *because* a blast went off — no disturbance in the treeline, no answering sound, no stirred undergrowth. That would host a wrongness on a surface the players themselves produce many times a night, which is the inoculation finding (fourth occurrence: oversized interiors, menu treeline, lake surface, fog) arriving at its highest frequency yet. |
| Per-player sensory asymmetry | **produced, not spent** | The starter perceives a cause; anyone swept from behind cover perceives only an effect. Falls out free, no `vfx-proximity` machinery built — same posture as #152, W5, R4 and the glow stick. |
| Canopy · Landform · Scale · Fog · Open sanctuary · Doorway · Stairs that lie · Non-predictive threshold · Route loss · A world that breathes · Persistent scarring · Tension-budget pullback · Teammate voice cut-out · Sound with no visible source · Rising tide · Proximity voice falloff · Authority withdrawn | **untouched** | Nothing here touches, spends, leans on or defuses any of them. |

### Feeling 1 — holding a full charge

**The honest finding first, because the packet's framing assumes a tension the mechanism does
not contain.** Charge costs nothing to hold, never decays, is never interrupted, refills for
free and resets to *full* on death. There is therefore **no hoarding pressure and no
when-do-I-spend-it agony** — you are charged almost always, and "carrying something ready" is
the default state of being alive rather than a held breath. A direction that claimed otherwise
would be directing a feeling the build cannot produce, which is the §6.0 defect Talon reported
about the treeline, invented in advance.

**Where the tension actually is: it is not aimed outward, and it is not about a threat.** A
full charge is the largest possible circle centred on *you*, it goes off radially at everyone
including yourself, and the chain threshold does not scale — so a fully charged eel is the
biggest fuse in the party and the party can see it on your body. **Holding a full charge should
feel like carrying something that is not yours to aim, in a room full of people you like.** The
decision the mechanism genuinely offers is *who am I standing next to*, and that is a real,
cheap, learnable decision available every second of the night.

**The five.** *Belief* — **this is mine and I choose when.** Every game this player has played
has taught that a full resource is a decision they own. *Break* — it is not theirs and they do
not choose: the eel beside them twitches and the charge is gone, spent by somebody who was not
thinking about them at all. *Why here* — it cannot move, because it is not placed: it is true
wherever two eels stand within a blast of each other, which under a single shared player species (canon §1.2) is
most of the night. *Who perceives* — everyone, continuously and identically, which is the R5
posture rather than the fetch's asymmetry: the tell is on every body and the whole party reads
the same fact, which is what lets them argue about it. *What changes* — the group learns that
their defence is **jointly owned**, and every later huddle is priced against it.

**Two requirements, not tuning.**

1. **No meter, no bar, no number, no count.** The charge is read off a body or it is not read.
   This is the fourth time this file has written that sentence (R4's recovery, W5's worded
   readout, R5's no-UI reading) and it is sharper here because the mechanism replicates a
   smooth `0..1` to every peer, which is a gauge already in the hand of whoever renders it.
   The tell must read as **fullness in a body** — a magnitude legible at party distance,
   never resolving into steps a player counts. **A stepped tell is a meter wearing a costume.**
   Per the lean law: under-explain. Nothing announces it, nothing labels it, nothing explains
   what it is for.
2. **The tell tells the truth and only the truth.** It is an honest-lane cue (§8.3's
   parenthetical): it states a fact about a body. It may never flicker, dim or brighten to hint
   at a creature, and it may never be modulated by threat state — see the prohibitions.

### Feeling 2 — a chain going off

**Two readings, and the mechanism makes them genuinely different.** For **the one who started
it**: the press is the only voluntary act in the entire kit, and the instant of authorship and
the instant of losing all authorship are *the same instant* — you caused it, and you
immediately stop being the author of anything, on a bearing you did not pick, for as long as it
takes to land. That is §4.5 agency and the joke in one move. For **the ones swept into it**: you
pressed nothing. A resource you never chose to have, that accrued while you walked, is spent for
you by somebody else's panic — **you are not hit by a blast, you are spent** — and the cost that
lands is not the tumble, it is that the whole party is simultaneously empty, scattered, and
blameless. Nobody aimed. There is no one to be angry at. That is the register: bumper cars, not
artillery.

**Where the dread is, stated plainly so the absurd is not mistaken for the whole thing.** It is
not in the bang. It is in the three seconds after: six bodies on their own bearings, out past
the light they were standing in, each blind, none of them where they chose to be, and every one
of them holding nothing. The slapstick is what the clip shows; the dread is what the *party
position* is when the laughing stops. This is why the kit must be spent against something —
see the §3.1 gate.

**The five.** *Belief* — **what happens to my body is caused by the world, or by me.** *Break* —
it was caused by a **friend**, who was not aiming, and who is also airborne. *Why here* — it
cannot be placed and must not be: it happens wherever the party huddles, and a chain staged at a
location would be §8.4's set-piece the first time it repeated. *Who perceives* — **everyone, in
the same instant, and all of them inside it.** This is the ledger's first event with that
property and it is recorded as such in the spike gate below. *What changes* — the party is
empty, scattered, and has to walk back together; and the group now knows, in a way no
explanation would have taught, that standing close is a decision.

**One requirement, and it decides whether this feeling exists at all: a chain must be felt as a
SEQUENCE, not as one bang.** The whole wave resolves inside a single server tick and ships as a
single message, which is correct mechanism and must not be changed — but if the presentation
delivers the releases as one indistinguishable crash, the cascade nobody scheduled has been
thrown away and the device is a loud noise. The second and third releases are the beat; the
first is only the cause. **This needs no mechanism change** — the released set, the origin and
the charge are already in the one broadcast, and ordering them is presentation's to author. **If
anyone proposes splitting the chain across ticks to get the sequence, that is a mechanism change
and goes back to systems-design, not into a presentation packet.**

**Dread gate.** *Ambiguity* (§2.1) — **pass on one narrow axis, FAIL on the axis that matters,
and the split is this pass's most important finding.** Nothing about this kit is ambiguous: the
charge is a pure function of time, visible to all and incapable of lying; blast membership is a
binary circle; the chain is deterministic and provably terminating. It spends no fuel and adds
none. The one genuine ambiguity is **where you land**, which is uncertainty about *position* —
the same legitimate shape the land pass reached, and explicitly *not* the same thing as
uncertainty about **presence**, which is what §2.1 is made of. **Stated in the section's own
words: this kit is not a dread device and must never be sold as one.** Its register is release.
*Clock* (§2.2) — **pass by inheritance only, and the counterfeit here is the sixth and the first
one that is genuinely convincing.** The fuel state (#152), the chill (W5), prone time (R4), the
dwell timer (R3) and the glow stick's burn-down each failed at least one of §2.2's four tests.
**The eel's charge passes all four** — it is visible on the body, shared with every peer,
monotonic, and inevitable — and it is still not a clock, because §1 defines dread as an
approaching **bad** outcome and this one approaches a *good* one. **It is a countdown to relief,
which is a clock running backwards.** The guardrail that follows is live and easy to trip: the
moment anyone gives the full meter a bad end — an overcharge, a forced discharge, a "it goes off
on its own if you wait" — it becomes a real second clock and walks into §13's *one clock or two*
fork by accident. *Break* (§3.1) — **FAIL, eighth consecutive entry, and the first time it fails
in the opposite direction.** Every prior entry failed because nothing bad could happen, so the
build never broke. This one fails because **there is no build.** The kit is a release instrument
with no tension of its own: charge costs nothing, threatens nothing, and accumulates nothing for
the blast to be a release *of*. A release with no build is §8.2's startle — a bang that is a
bang. **The consequence is the most useful sentence in this entry: the eel supplies the absurd
half of §9's ratio and none of the dread half, so it must be spent against builds that other
systems own** — the walk, the quota, the night, whatever eventually threatens the party. Point
it at those windows rather than authoring a fresh one. *Incompleteness* (§3.2) — **pass, and
unusually strongly.** A chain resolves this instant and leaves the night standing, and it leaves
the party **worse off than before it fired**: everyone empty, everyone scattered. A release that
costs you something is the opposite of an all-clear. *Degradation* (§3.4) — **FAIL, and it is the
cleanest instance of §8.7 in the file.** The charge is the first safety in this game that
recovers **automatically, on a timer, for free, forever, without the player doing anything at
all**, and canon 14 deliberately restores it to full on respawn. It is the exact inverse of the
glow stick, which this ledger records as the first safety that genuinely runs out and that the
player bought. It is worse than R4's portable hide in one respect: the hide at least required a
thumb. **The mechanism's own argument against decay is sound and is not overruled here** — the
fix is not "make it leak". A fork with its trigger is added to §13 instead. *Affect* (§6.0) —
**pass, conditional, on the two conditions above** (fullness felt not read; the chain felt as a
sequence). The test for feeling 2 is not "does the capture show bodies in the air"; it is *can
you tell, without counting, that more than one thing went off.* *Fairness* (§7.1, §7.2) —
**pass, with one floor.** §7.1 avoidable-but-barely: a chain is genuinely avoidable by not
standing in the huddle, which is cheap, learnable and available every second. §7.2 is exemplary
in the mechanism (one circle, one threshold, no randomness in membership) and carries one
requirement here: **the random bearing must never be mistakeable for a random rule.** A player
must always be able to reconstruct *I was caught because I was inside it*; only *where I flew* is
unknowable. Confusing those two is the precise line between dread and frustration, and it is the
one place this kit could cross it.

**Spike gate: no spike claimed, and one is refused — but one condition passes for the first time
in this ledger and that is worth more than the refusal.** *Stake* (§4.1.1) — **FAIL**: nothing is
on the line, because the charge protects against nothing and costs nothing. That is what sinks
it. *Violation* (§4.1.2) — **pass**, emphatically, for anyone swept. *Simultaneity* (§4.1.3) —
**PASS, and it is the ledger's first and strongest instance.** Every prior candidate failed or
dodged this condition: the watcher fails it *by construction* (it faces one player), the lake
fails it (§1.1 guarantees nobody is there), the glow stick fails it (only the walker perceives),
the panic drop merely makes it *available*. A chain is the first event in this game that two to
six players perceive in the same instant **and are all inside**. *Legibility* (§4.1.4) —
**conditional** on the sequence requirement, and note that a stranger watching the clip would
read *what went wrong* perfectly and *what was at risk* not at all, which is condition 1 failing
again from the outside. Not a spike, and staging it as one would be §4.4 inflation and §8.4's
set-piece in a single move. **But it is the best spike substrate the ledger has recorded for
simultaneity specifically**, and the standing instruction applies: whatever eventually puts
something at stake should be pointed at this window rather than at a fresh one.

**Prohibitions, all nine.** §8.1 over-exposure — n/a, nothing is shown, with **one hard guard**:
nothing about a blast may be authored to reveal, illuminate or resolve anything in the dark. A
discharge that lights the treeline would spend the watcher's entire device and four deposits'
worth of inoculation discipline in one frame. §8.2 startle as build — **the live prohibition and
the sharpest in this pass, because the kit is a startle generator by construction.** Three
constraints: a blast is a **release** and may never be used as the engine of dread; nothing may
be authored to fire *at* a player as a scare; and the chain's later releases may not be staged as
escalating stings — they are a sequence, not a crescendo. Nothing fires at the landing either;
the skid is the comic tail, not a second bang. §8.3 reliable telegraphs — **not violated, with the
guardrail on its fourth writer.** The charge tell and the blast crack are both `LEVEL-BIBLE.md`
§8.2 *honest-lane* cues and must stay honest: **neither may ever flicker, dim, brighten or be
modulated by threat state to hint that something is out there.** #152's firelight, R5's lit
radius and the glow stick's burn-down each carry this same guardrail, and a fourth writer on one
lane is exactly when a guardrail gets broken by accident — recorded in those words. §8.4 scripted
set-pieces — **not violated**; a verb is a possibility space, the variation lives in who was
standing where, and nothing may be authored to fire at a fixed point inside a chain. §8.5
illegible chaos — **the live risk, fifth occurrence** (canopy, land, lake, glow stick, now this),
and the most literal: six bodies, several bangs and a pinball is one dial from noise with no
discernible cause. The mechanism supplies the cause; **presentation is where this becomes chaos**,
and the sequence requirement is the single thing standing between this device and §8.5. §8.6
unrelenting tension — **n/a and structurally healthy**: the kit manufactures valleys, which is
the thing the ledger has been short of. §8.7 permanent safety — **FAIL** (see §3.4), and the
second *renewable* instance after R4's portable hide, worse in one respect: R4's hide cost a
tiring thumb and this costs nothing whatsoever. §8.8 streamer-first — **pass, with the sharpest
named temptation since the savanna.** Six animals pinballing off trees is the most clippable
object anyone has proposed in this repo, and the kit's own framing (*it must survive an
out-of-context clip*) sits one step from designing *for* the clip. The test, borrowed unchanged:
**is this for the six people in the dark at 2 a.m., or for the trailer?** No authored camera
move, no slow-motion, no scored beat — a chain is funny because it happened to them, or it is not
funny. §8.9 punitive failure — **pass, and the mechanism earns it**: no dropped haul, no knockout,
no injury mark, a bounded tumble. **Forward constraint:** a chain must cost **time and dignity,
never progress.** The day someone makes it drop a haul or cost a quota, the valve inverts and
"we all went flying" becomes "we wasted an hour."

**§9's ratio is untouched, and this pass contributes an input rather than a vote.** §9 requires
the director to build sincere dread first and to *flag*, never resolve, any beat whose absurd
payoff would have to be invented. Here it did not have to be invented — Talon supplied it — and
the kit is the first **instrument** in the repo that is primarily absurd, which means the ratio
question finally has something to be asked about. It is also, per the §3.1 gate, an absurd payoff
with no dread build attached to it, so the ratio cannot be read off this feature alone. Flagged,
not resolved; the trigger in §13 is unchanged.

**Canon updated:** this entry (new); one new §10 row (*Contagious panic*); one §10 row amended
(*Forced separation* — first forced host, still not a spend); one new §13 fork (what makes the
eel's charge cost anything). No fork resolved. §6.2 is not moved and its constant-naming defect
is noted, not fixed. §11 is untouched — nothing here has landed in a session, and a signature is
observed, not declared.

**Handed off, unanswered — implementation, and none of it is this file's.** How the charge reads
on a body and how the blast reads at every distance (`vfx-*`, against the two affect conditions
as acceptance tests); how a one-tick chain is delivered as a legible sequence, and the crack's
place in the night mix (`sound-*`, entry point `sound-integration-guide`; `vfx-audio-sync` rations
anything that touches the bed); every value, every duration and every colour (playtest, and the
director does not pick); the denial cue's channel, which is `INTERACTION-BIBLE.md` §8.4's live
blank and stays Talon's; and **the entire creature-response question, which is canon 7's and is
routed to `/spec-entity` at the first predator's own packet** — §0 is explicit that directing a
feeling never grants a capability, and this pass granted none.

---

**The night jobs — directed 2026-08-14 (packet NIGHTJOBS-B), pre-build, two passes in one
sitting: the radio cord and the star fix.** Frame supplied by Talon, 2026-08-13/14: *"the night
has jobs — you go out to KEEP things alive, not to take things."* The register consequence is
worth stating first because it changes what every future night beat is directed against: this
file's whole apparatus was written for a night the player **enters to get something**, which is
the posture of all three comps. A night the player enters to **maintain** something inverts the
stakes without changing a single mechanic — the loss is no longer *a thing I failed to obtain*
but *a thing I already had*, and §2.1's ambiguity now attaches to something the group is already
attached to. Both passes below are directed at `system` altitude and both author a **setup**, not
a beat. Neither is built. Neither resolves §6.2, §9's ratio, or any §13 fork.

| Device | Status in this direction | Note |
|---|---|---|
| Teammate voice cut-out | **amended, deposit under construction — NOT spent** | The 2026-07-25 block ("no afterlife channel") is not lifted; the cord opens a *second door* to the same perceptual event with nobody dead. Three constitutive constraints in the §10 row, chief among them that **bare silence fails §4.1 condition 4** and the break needs an audible artefact on both ends. |
| Forced separation | **amended — first payment against the PRECONDITION, still not a spend** | Six entries have now recorded this row as blocked on "togetherness is not worth anything." The cord is the first mechanism that makes it worth something. The cord does not force; it makes forcing *cost*. |
| Proximity voice falloff | **amended, not spent** | The cord is falloff's first exemption, and an exemption is what turns a limitation into a law. **Refusal: the cord may never widen the range or scale attenuation** — a stretched curve makes the break a fade, and a fade is not §5.5. |
| Infrastructure as the attack surface | **NEW ROW — directed, unbuilt** | The group's own construction as the thing the world reaches for. Registered now, before hearths, light lines and rock rings each invent it privately and burn it three times over. |
| Cost-to-know instrument | **amended — first genuine INSTRUMENT candidate** | The star fix. Readable only where the sky is open; the open sky is also where canon 4 makes you a silhouette. Alien's motion tracker with both halves already shipped and no gadget authored. |
| Landform as disorientation | **reinforced, not spent** | Constraint (b) generalised into a standing rule for every wayfinding channel: **answer a question, never answer where home is.** Stars answer *north*, drainage answers *down*, the cord answers *where is my line*. Four refusals attach to the cord and each one alone deletes the device. |
| Canopy as spatial darkness | **reinforced, not spent — and now PRICED** | Constraint (e) added. The sky-denial buys a currency instead of merely being a cost. **The exchange runs one way: the sky pays for the clearing, never the reverse.** No graded partial fix under thinning cover; **no second position-keyed darkness system, ever** (this is where the always-night tube pocket is bound — it runs this field at maximum or it is not built). |
| Position without judgment | **applied, not spent** | The governing rule for both passes. A dead line may say *the line is cut*; it may never say *where*, *what did it*, or *whether it is still there*. A star fix may say *north*; it may never say *safe*. |
| Per-player sensory asymmetry | **leaned on, not spent** | The two passes compose: a fix is information exactly one player holds, in a place only they are standing, which is worth nothing until they **say it out loud** — down the cord. That is §5.4 feeding §5.1 with no third system. |
| The unreadable dark | **not drawn on** | Nothing here fires an event into the dark. The lake and menu deposits are untouched, as always. |
| Night reversal / §6.2 | **not moved** | `MinAmbientEnergy` untouched. Neither pass needs a darkness value and neither picks one. |
| Wrong silence | **explicitly NOT this, and the confusion is one word away** | A cut comms line is a **caused, attributable, player-relevant** loss of a channel. §6.3 requires withdrawing a sound whose absence has **no explanation**. Logging a cord break as wrong silence would burn the file's strongest unspent row on an event that defuses it, exactly as the campfire entry already warns about the crackle stopping. |

**Dread gate — the radio cord.** *Ambiguity (§2.1)* **pass**, conditional on *Position without
judgment*: the instant a break reports a distance, this fails. *Clock (§2.2)* **pass by
inheritance only** — the cord authors no clock and **must never be given one**; a "line battery"
or a decaying signal would be a second clock and goes through §13's *one clock or two* fork
first. *Break (§3.1)* **pass** — and it is the rare build that breaks without costing progress.
*Incompleteness (§3.2)* **pass**, on the constraint that repairing a break never explains it.
*Degradation (§3.4)* **FAIL as naively built, pass under one constraint** — a cord laid once and
kept forever is §8.7's permanent safety made of information; the line must be a finite paid-out
resource and a repair must cost more than the original lay. The *shape* is required here; the
*values* are not this file's. *Affect (§6.0)* **pass** — §5.1 names voice the highest-leverage
instrument in the game and §5.5 names its subtraction the hardest-landing interpersonal device.
*Fairness (§7.1, §7.2)* **pass, conditional** — a line that breaks with no learnable cause is
§7.2's "behaviour changes without cause," which players correctly read as broken rather than
mysterious.

**Spike gate — the radio cord, and a spike IS claimed for the break.** *Stake* **pass**, but not
on night one: the stake is the belief, and the belief takes nights to install. *Violation*
**pass** — mid-sentence is the violation. *Simultaneity* **pass, structurally** — both ends lose
the same second by construction, which is the only device in this register that satisfies §4.1
condition 3 without an author placing two players in a room. *Legibility* **FAIL as bare
silence, pass with the artefact** — this is the pass's single most important finding, and §4.2's
rule is quoted rather than paraphrased: the fix is a sharper audible trigger at the moment,
**never more ambience**. The artefact is the **line's** sound and never the **cause's**.
*Retroactively explicable (§4.3)* **pass, and unusually strongly** — walking back to the cut end
*is* the reconstruction; the story assembles itself out of the walk, which is the closest thing
in this file to "the causeway flooded behind us." *Agency (§4.5)* **pass** — you chose how far to
push the line.

**Dread gate — the star fix.** *Ambiguity* **pass**. *Clock* **pass by inheritance; the sky must
not become one** — a sky that reports how much night is left is a second clock, refused here.
*Break* **FAIL, stated rather than papered over** — a star fix authors no break. It is an
instrument, and the break belongs to whatever the player finds out with it. *Incompleteness*
**n/a**, no release. *Degradation* **pass on the memory-only constraint**, and that constraint is
the whole design. *Affect* **pass** — looking up, in a game whose environmental apparatus has
spent a year denying the sky, is an event before it is information. *Fairness* **pass, and
cheaply**: the sky is honest, identical for every player, needs no server state, and satisfies
the parity law for free. **No spike claimed**, and none should be manufactured.

**Prohibitions, all nine, named.** **8.1 over-exposure** — the thing that cuts a line is never
seen cutting it; you find the ends. **8.2 startle as build** — the severance artefact is a
release and must not be loud, repeated, or used to build. **8.3 reliable telegraphs** — the
sharpest risk in the whole direction: if only the threat cuts lines, a dead line is a perfect
alarm and the game gets *safer* the longer it is played. At least one cause must be indifferent.
The sky is separately bound: it never dims for a creature. **8.4 scripted set-pieces** — no
scripted first break, no night-two beat; the possibility space, and the players supply the
drama. **8.5 illegible chaos** — answered by the spike-gate condition 4 finding; a bearing is
maximally legible on its own. **8.6 unrelenting tension** — the intact line is a *genuine
comfort* and must be allowed to be one, because that is the entire setup. **8.7 permanent
safety** — two live failures caught and constrained: the cord as free permanent infrastructure
(§3.4 above) and a retained star bearing as safety made of information. **8.8 streamer-first** —
neither pass is designed for the camera; the clip falls out of the walk or it does not come.
**8.9 punitive failure** — a dead line may cost time, dignity and a walk; **it may never fail a
night's quota on its own**, which is the sixth independent filing of this same constraint.

**§9's ratio: untouched, and this pass supplies an input, not a vote.** Both passes are sincere
dread with no absurd payoff invented for them — correct under §9's standing instruction. The
absurd payoff in this program lives entirely in the third piece (the stolen handset keying its
mic from the dark), which is **spec-track and unbuilt**, so it contributes an intention and not
a data point. §9's trigger is unchanged.

**Canon updated:** this entry (new); one new §10 row (*Infrastructure as the attack surface*);
six §10 rows amended (*Teammate voice cut-out*, *Forced separation*, *Proximity voice falloff*,
*Cost-to-know instrument*, *Landform as disorientation* — including the standing wayfinding rule
— and *Canopy as spatial darkness*, constraint (e)); two new §13 forks. **No fork resolved**,
including the one this program most obviously touches: whether the comm line and the light line
are one object is **the Light Line scope fork, and it is Talon's**, flagged in the program doc
and deliberately not answered here. §6.2 is not moved. §11 is untouched — nothing has landed in
a session.

**Handed off, unanswered — and none of it is this file's.** Every value, distance, duration and
lay-rate (playtest, and the director does not pick); the severance artefact's actual sound and
its place in the night mix (`sound-*`, entry point `sound-integration-guide`, with
`vfx-audio-sync` rationing anything that touches the bed — and note the bed may **not** react to
a break, per *Position without judgment* constraint (b)); how a cord reads at a distance without
becoming a beacon (`vfx-*`, against the four refusals as acceptance tests); what a fix *looks*
like when taken, which must not become a UI (`INTERACTION-BIBLE.md` §8.4's live blank, Talon's);
the entire question of **what breaks a line**, which is a creature capability and is routed to
`/spec-entity` at the first predator's own packet — §0 is explicit that directing a feeling never
grants a capability, and this pass granted none.

---

**The postpile — directed 2026-08-13, previz built (the second anomaly pocket; Talon asked to
see the Devils Postpile in the game and a walkable previz exists at
`scenes/dev/PostpileLab.tscn`, captures in
`docs/superpowers/status/2026-08-13-postpile-previz/`).** Altitude: **level** — a bounded zone
with an approach, an interior and an exit, directed pre-build in all three. A `system` pass is
flagged for nothing, because this pocket authors no system: that is most of what recommends it.
Register, per Talon's 2026-08-13 ruling: wonder and dread together, absurd on top — and here
the absurd payoff is **not invented**, per §9's still-open ratio. The day register carries
wonder alone and that is allowed.

The direction in one line: **the woods open onto a floor that looks laid and a wall that looks
built, and there is nobody in this world who builds.**

**On the bounded exception's first spend, ruled explicitly because the packet asked.** This is
a **second spend, not a co-spend and not a reservation.** The §10 row's own terms authorise it
— *"Each pocket type decays independently"* — so the savanna's spend burns the savanna's law
and not the device. What the row's constraint (c) *rationed per run* does bind is **placement,
not existence**: two pocket types may both ship; they may not be in sight of one another, or
the eye reads one erosional family and the second is a variation instead of an exception. That
is a macro-pass spacing rule and it is filed as one. The device's game-wide state therefore
moves `unbuilt` → **two level-altitude spends, neither built**, and the third pocket type will
need a harder argument than this one did.

| Device | Status in this level | Note |
|---|---|---|
| The bounded exception | `spent-here` — **its second spend** | The pocket's engine. One law: the stone here is hexagons. Nothing else strange in frame; the pines are the ordinary woods and are in shot on purpose. |
| **The apparent artefact** | **new row — directed, nothing built** | The postpile's second violation and the reason it earns a row rather than borrowing one. See §10 and the anti-inflation check below. |
| Canopy as spatial darkness | **inverted at the boundary, not violated — same posture as the savanna** | No second position-keyed darkness system is authored. The dome is the *removal* of the first, in a room with an edge; the sharp rim is what keeps "darker means deeper" honest as a woods rule (§7.2). **One new hazard the savanna did not have:** the savanna's opening is roofless *and* blind, so it costs as much as it gives. This one is roofless and NOT blind, which makes "open sky returns sight" a live question about the sight model. **Not resolved here, and deliberately not duplicated into §13** — it is `docs/WORLD-ANOMALY-POCKETS.md` §4 fork 5, with its trigger, and one copy is the rule (`LEVEL-BIBLE.md` §1). §6.2 is untouched and is a different fork. |
| Landform as disorientation | **NOT spent, and the compass rule bites hardest here** | The land pass's constraint (b) is that the world's honest compass must never point home. A colonnade wall visible above the treeline is the most powerful fixed reference this map has ever contained, and it is one careless placement decision from being a beacon. **Requirement: the postpile may tell you where YOU are and must never tell you where CAMP is.** It is a fixed point with no relationship to home, and if the macro pass ever places it on a sightline from camp it has handed the woods the one answer the land pass spent a whole direction taking away. This is also the exact hazard the pillar field carries, and the two must not both be granted it. |
| Scale that is not the player's | **INVERTED, and that inversion is the point — not re-spent** | Every prior host left the 1–3 m human band empty so the space would read as not-built-for-you. This place fills the band precisely: a 0.6–1.1 m tile is a stepping stone, a pavement is a floor, and it is *exactly* your size. The row's own logic is what makes the inversion work rather than break it — a space that gives you nothing to read yourself against is not-for-you; a space that fits you perfectly, in a world where nothing else does, is worse. **The row is not re-spent and its constraint is not relaxed anywhere else**; this is one room that reads the other way on purpose. |
| The unreadable dark | **night posture only — deposit discipline, NOT spent (fifth surface)** | The weather/events carve-out transfers verbatim. Wind over stone is weather. A shape on the pavement, a sound from the talus, a block that has moved between visits, anything at the top of the colonnade — all events, refused outright. The pavement is the most legible ground in the game and therefore the most expensive place in the game to put something. |
| Wrong silence | **REFUSED — eighth occurrence, and a new failure mode** | Do not wire `AmbientBed.Withdraw()` to the pocket boundary. Worse than the savanna's version: stepping from duff onto bare stone changes the footstep material, so the acoustic change is not merely *caused*, it is caused by the player's own feet, once per step, forever. That is the most explained absence this ledger has yet been offered. The postpile's bed is **different weather** — wind with nothing to rustle in it, and footfall on rock — never an absence. |
| Colour temperature as threat channel | **untouched — seventh occurrence of the same trap** | Grey stone under a moon wants to be graded blue, and cool is the warning direction. The rock reads *paler and flatter* at night, never bluer. The dusk sweep's grade stays the sole warning lane. (The previz's own night frames lean this way and are lab lighting, not a direction — see the handoff.) |
| Accumulating wrongnesses | **NOT hosted, deliberately — same as the savanna** | One law, nothing else in frame. The postpile is not where a wrongness lives; it *is* one, once, at landscape scale. Specifically: **no second geometric formation, no cairn, no alignment, no arrangement.** See the new row's constraint (b). |
| Night reversal / Day-night cycle as clock | **borrowed, not re-spent** | The pocket authors no clock and no darkness of its own. |
| Fog as sightline denial | **not spent, and it is not available here** | That row's reuse rule requires something approaching; nothing does. It is also the pocket's defining absence — see the cost note in the pockets doc: this is the first pocket with nothing to hide repetition behind. |
| Glow-stick trail | **noted, not spent** | The pavement is the only surface in the game where a dropped stick sits on bare rock with no undergrowth, so a trail across it reads at its cleanest. The lean law forbids staging that image as it forbade staging the savanna's. |
| Refuge maintained only by leaving it / Open sanctuary inverted by dark / The ration that never refills | **untouched — and one live §8.7 hazard this pocket creates and the savanna did not** | The savanna entry could write *"the pocket is not a sanctuary (no cover, no warmth, no refuge)"*. **This one cannot.** Flat, open, drained, free of undergrowth, elevated, with a 15 m wall at your back is the best defensive ground on the map, and players will find that in one night. It is not a refuge the *design* grants — it is one the *geometry* grants for free, which is the harder kind to notice. Routed as a finding, not resolved: see the handoff. |
| The watched / any creature | **protected, not placed** | Nothing here grants placement or capability. Whether any creature may enter a pocket is its own contract's question, and the savanna's §8.3 flag applies unchanged. |
| Per-player sensory asymmetry | **produced, not spent** | The finder is standing on a floor and describing it as a floor; camp hears a teammate say "it's tiled" and cannot tell whether that is a joke. Falls out free, no machinery. |
| Doorway as proscenium · Stairs that lie · Non-predictive threshold · Route loss · A world that breathes · Persistent scarring · Tension-budget pullback · Teammate voice cut-out · Sound with no visible source · Synced audio-visual peak · Flash beacon · Cost-to-know · Forced separation · Contagious panic · The blind hide · Attention as a resource · Authority withdrawn · Position without judgment · Rising tide · Proximity voice falloff | **untouched** | Named individually so the refusal reads as a check rather than an omission. Nothing here touches, spends, leans on or defuses any of them. |

**Anti-inflation check on the new row, run before it was added.** Four rows are adjacent and
none covers it. *The bounded exception* is about the **room** — one landscape law, enforced,
with an edge; the postpile's extra charge is not that its law differs but that its law produced
something that looks intentional. *Accumulating wrongnesses* (§6.4) compounds small dissonances
and the lean law forbids compounding — this is one dissonance, once. *Traces, not exposition*
(§6.5) introduces a thing by what it left behind, and every trace implies a maker who exists;
this implies a maker who does not, and that inversion is the whole content. *Scale that is not
the player's* is "not built **for** you"; this is "**built** at all." The row goes in.

**The five.** *Belief* — two of them, and this is the only entry in the ledger to break a
second belief with the same object. (i) The woods are the world (five directed passes taught
it). (ii) **Nothing in this world was made.** Every structure the players have met is a camp
building they can name — cabin, lodge, store — and everything outside the camp is grown or
fallen. *Break* — the treeline opens onto a paved floor. Not a clearing, not a rock: a floor,
tiled, level, fitted, with a step up onto it. *Why here* — it must sit deep, for the savanna's
reason (canon #5's deepening wood range walks players into it under pressure; found by tourism
it is a postcard) and for one of its own: **the belief that nothing is made is only strong far
from the camp that disproves it.** Ten metres from the lodge a paved floor is a patio. Two
hundred metres into the woods it is a question. Move it shallow and the beat is not weakened,
it is deleted. *Who perceives* — whoever walks in; the retelling travels by proximity voice,
and this one has an unusually good sentence attached to it, which is why §8.8 is the sharp
prohibition again. *What changes* — the group acquires a place they will name themselves and
will give each other directions to, and "somebody was here" becomes a thing one player believes
and another does not. The game never adjudicates that, ever.

**Approach / interior / exit, directed.**

- **Approach.** The setup is enclosure and the payload is a *surface*, not a vista. The colonnade
  wall may be visible above the treeline from outside (it is the pocket's honest silhouette and
  §4's legibility likes it) — but **the pavement may never be previewed.** Coming up the lower
  bench you see a cliff, which the world already contains cliffs to explain; the floor on top of
  it is the thing that has no explanation, and it must be met by walking onto it. The two halves
  of this pocket are two different discoveries and giving away the second from outside spends
  both at once.
- **Interior.** The feeling is **unearned welcome**. It is the easiest ground to walk in the
  game — flat, drained, clear of understory, quiet underfoot in a way duff is not — and the
  register is that the ease is suspicious rather than restful. Nothing enforces that and nothing
  should: no cue, no sting, no shift. §2.1 is satisfied by the place explaining nothing, and the
  interior's whole job is to be *pleasant and wrong at once*, which is the Talon register ruling
  delivered by construction. It must never resolve toward either.
- **Exit.** Two exits and they are not equivalent, which is the pocket's one genuine geometric
  idea. Walking off the north rim is a step down onto duff and out — a door, both ways, per the
  savanna's constraint 1. Walking toward the south rim is a **15 m drop the top of the pavement
  does not obviously announce**, because a domed floor hides its own edge. That is a §7.1
  fairness problem the instant the fall costs anything, and the requirement is that **the rim
  must telegraph itself from on top, by geometry the player learns once** — the sunken and
  weathered tiles thin toward the edge, the sky opens ahead, the sound changes. Learnable and
  consistent (§7.2), never a scripted warning (§8.3's honest-lane distinction: this is a
  fairness cue, so it stays reliable and never lies).

**Constitutive constraints (requirements, not tuning).**

1. **It says where you are, never where camp is.** The Landform row above. Placement must not
   put it on a sightline from camp, and no in-world element may relate it to home.
2. **The rim announces itself from the top.** The exit direction above; §7.1's floor.
3. **Nothing is ever on the pavement.** No events, no shapes, no sounds from the talus, nothing
   that has moved between visits. The Unreadable-dark row; the fifth surface to inherit the
   carve-out.
4. **Nothing confirms a maker, ever.** No carving, no symbol, no alignment to anything, no tile
   that was placed. The new row's constraint (b), and the single line between this pocket and
   lore.
5. **The variety is load-bearing, not polish.** No fog hides anything here (the pockets doc's
   cost note). A pavement that reads as tiling has failed the pocket, not disappointed it — the
   previz needed weathered-out sockets, fallen blocks and a patchy polish/matte split before it
   stopped reading as a pattern, and that work is in the build budget, not a later art pass.
6. **One law.** The stone is hexagons. Not hexagons *and* something.

**Dread gate.** *Ambiguity* (§2.1) — **pass, by construction**; the lean law is §2.1 as a build
rule. What it spends: one violation of "nothing here was made", on first entry, per group.
Revisits settle into a wonder-room, which is the intended register (§10). *Clock* (§2.2) —
**pass by inheritance only**; the pocket authors none, and alone it is a place, not dread — the
dread arrives with the night, the wood quota and the walk home. *Break* (§3.1) — **positional
pass, threat-break FAIL carried forward, eighth consecutive camp-adjacent entry**: stepping onto
the stone is a real level-altitude break; nothing in the build can yet make being here cost
anything. *Incompleteness* (§3.2) — **pass**: it answers "what is out here" with a fact and no
meaning. *Degradation* (§3.4) — **FAIL, and this is the entry's headline finding.** Seven prior
entries could write *n/a — not a sanctuary*. This one is a sanctuary the geometry hands over for
free, and §8.7 is failed by terrain rather than by design. Not resolved here; routed. *Affect*
(§6.0) — **pass, conditional**: standing on it must *feel* like standing on a floor — footfall,
the tiles underfoot, the absence of understory. If the honest playtest answer is "it's a grey
area," that is the treeline defect in a new place, and the fix is the surface, never a palette.
*Fairness* (§7.1, §7.2) — **pass, with constraints 1 and 2 as floors.** The rules are geological
and identical everywhere the law is rolled; variety is about the map, never the rules.

**Spike gate: no spike claimed.** A place is not a beat — fourth time (canopy, land, savanna,
here). Substrate noted, not claimed: a group that has to cross the pavement at night with a
dying trail has stake, agency and simultaneity for free, and whatever eventually threatens the
walk should be pointed at a window like that rather than at a fresh one.

**Prohibitions, all nine.** **§8.1 over-exposure** — hard refusal: nothing is ever on the
pavement, on top of the colonnade, or in the talus; the *rock* may be categorised freely (that
is constraint (a) of the new row and it is what keeps the place honest), a **maker** may never
be. **§8.2 startle as build** — live constraint: the threshold is spatial. No sting, no audio
cue, no light snap on entry; the door is walked through. **§8.3 reliable telegraphs** — the
savanna's forward flag applies unchanged (the first packet that reliably places evidence,
spawns or creatures in pockets converts wonder rooms into telegraphs), plus one local
distinction: the rim cue in constraint 2 is a **fairness** cue and therefore stays honest and
never lies, which is the §8.3 parenthetical applied exactly. **§8.4 scripted set-pieces** — not
violated; per-run macro placement keeps discovery emergent and nothing is authored to happen on
first entry. **§8.5 illegible chaos** — pass in the interior (one law makes it the most legible
ground on the map); the risk is at the boundary, where a blended edge is mush. **§8.6
unrelenting tension** — healthy; the pavement by day is a genuine valley and that is allowed.
**§8.7 permanent safety** — **WORSENED, and it is the first entry in the ledger that has to say
so.** The pavement is free defensible ground. Routed. **§8.8 streamer-first** — the named
temptation again: no authored vista, no camera move, no staged reveal, and specifically **no
approach composed so the pavement is framed on arrival**. The players compose the image or
nobody does. **§8.9 punitive failure** — n/a today; forward constraint: going off the rim costs
**time and dignity, never progress**, which is the standing constraint the land, watcher,
glow-stick, savanna and eel passes each filed, and it is already what the knockout law provides.

**Canon updated:** this entry (new); one new §10 row (*The apparent artefact*); the §10
*bounded exception* row amended to record the postpile as its **second** spend and to state the
placement rule that follows from constraint (c). **No fork opened here and none resolved** —
§6.2 is untouched and unmentioned as a decision, §9's ratio is untouched (no absurd payoff is
invented; the day register carries wonder alone), and the pockets doc's forks 1 and 3 stay
frozen. The open-sky-vs-blind-night question raised by this pocket is filed once, in
`docs/WORLD-ANOMALY-POCKETS.md` §4 fork 5 with its trigger, and deliberately **not** copied
here.

**Handed off, unanswered — implementation, not direction:** column diameter, mass size, face
height, talus depth and the pocket's placement depth (macro-pass values; the previz's numbers
are drawn from the published Devils Postpile surveys and are a starting point, not a ruling);
**whether the pavement's free defensible ground is accepted, priced, or designed out** — a
`/spec-level` and `MECHANICS-BIBLE.md` question and the most important thing this pass hands on;
whether the pocket holds gatherables so necessity rather than curiosity routes players through
it (`/spec-level`, canon #5); how the rim telegraphs itself in geometry (`/spec-level` + the
builder, with constraint 2 as the acceptance test); the surface's footfall lane
(`sound-soundscape-construction` — the postpile is the first non-duff walking surface outside
the camp buildings); the night read of the colonnade silhouette and the pavement under the
darkness law (`vfx-lighting`, with the previz's lab lighting explicitly **not** a direction —
it exists to show the rock, not to propose a night); and whether open ground returns sight at
all (pockets doc §4 fork 5 — Talon's, on its trigger).

**Bubble-test lake — directed 2026-08-27 (packet BT-9, Talon's words: "murky depths, a bit
ominous"). Altitude: `system`. The honest headline: this is ATMOSPHERE, NOT DREAD, and the
gate below says so in §2.2's own words.**

The bubble test is a bright, legible playtest level — a hub, four colour-coded activity
sections, a hundred harmless bubbles. **Every surface in it answers a question when you look
at it**, which is the level's job: it exists to test movement, popping and voice, not to
frighten anybody. The lake is the one surface that returns no answer. That is the entire
direction, and its whole value is that it is *one* surface rather than a mood laid over the
level.

| Device | Status in this level | Note |
|---|---|---|
| The unreadable dark | **deposit — NOT spent. Second host, same posture as the camp lake (W5, 2026-08-08).** | Nothing is ever withdrawn from this lake either, and the W5 constraint travels verbatim and is constitutive here: **nothing is ever shown in or on the water — no shape, no breach, no wake, no silhouette, no uncaused ripple.** A single authored event in this water converts a standing deposit into a spent startle (§8.2) and takes the camp lake's deposit down with it, because a player who has met one lake reads the other through it. Reuse in a second level is legitimate precisely because the row is a `deposit`: nothing has ever been drawn against it. |
| Colour temperature as threat channel | **spent-here, lightly, and it is the only genuine spend in this pass** | §6.6 says the *direction* of a shift must be meaningful. The level's palette is clean saturated toy colour on off-white (BT-9's own section materials); the lake is the one surface graded away from it, toward a cool organic green-brown that reads as *not made for you*. It is meaningful because it is the only exception in a level that is otherwise relentlessly primary. Do not spend this row again inside this level — a second cold surface makes the first one decor. |
| Night reversal | **leaned on, not resolved — `NightAmbientFloor` untouched, and §6.2 is not moved.** | The ported water shader carries separate day and night uniform sets, so the murk deepens after dusk as a consequence of the shipped grade, not as a new authority. That is a lean, not a resolution: the §13 fork's four sides all stay reachable, and W5's note that the lake is the best *venue* for the side-by-side session is strengthened by a second lake existing to hold it. This pass does not pick and must not be read as having picked (a) by inheritance. |
| Fog as sightline denial | **REFUSED as a reuse** | That row's reusability rule is explicit and self-enforcing: *"fog keyed to a clock is set dressing; fog keyed to the approaching front is the clock made spatial. Reusable elsewhere only where something is actually approaching."* **Nothing approaches at this lake.** Murk in water is not that row and must not be logged against it. |
| No new device row opened | — | The temptation was a row for *vertical* prospect denial (a surface you stand over and cannot see through, as against the fog row's lateral denial). **Refused as register inflation** — the same refusal W5 made for the same reason. *The unreadable dark* already covers ambiguity delivered with no event, and inventing a row for what a row covers is how §10 starts lying. |

**The five.** *Belief:* the level is legible; every surface here returns a value, and water is
the wet gap between stepping stones. *Break:* the first time a player on a stone looks **down**
and the answer does not arrive — a non-answer, not a scare. *Why here:* this is the only place
in the level with a vertical dimension the player cannot survey, and it is the only one where
denying prospect costs the level nothing, because water denies it for a reason players already
accept. It cannot move fifty metres: it is bound to the one substance that makes opacity
ordinary. *Who perceives it:* **deliberately not simultaneous.** The stepping-stone gaps are
1.4–2.2 m, so the crossing is single-file — each player meets the non-answer alone while the
group watches from shore, which is §5.4 asymmetry for free and the correct shape for a
non-spike. *What changes:* nothing mechanical. The level acquires one surface that does not
answer, which is what makes the legibility of the other twenty a **choice** rather than an
accident.

**Dread gate.** *Ambiguity §2.1* — **PASS**; the murk resolves nothing and, by the constraint
above, nothing is ever placed in the water to resolve it. Spend: none; this is a deposit.
*The clock §2.2* — **FAIL**, and stated in the section's own words: there is no pressure driver
in this level and nothing about the lake is approaching, so **this is atmosphere, not dread.**
That is the correct outcome for a movement playtest and is not a defect to be fixed by adding a
clock nobody asked for. *Break §3.1* — **FAIL**; there is no build and nothing breaks. A murky
lake is a standing condition, not an arc. *Incompleteness §3.2* — **n/a**; no release exists to
be incomplete. *Degradation §3.4* — **n/a**; the lake is not a sanctuary and hosts no recovery
window. *Affect §6.0* — **PASS, conditionally, and this is the one gate the build can actually
fail.** The murk must read as a **denial of depth**, not as a dark texture: a player on a stone
must be unable to tell *how far* the bottom is while remaining certain there **is** one. A flat
black surface fails this — it reads as painted floor, and a cue that is perceived and feels
like nothing is a §6 defect. *Fairness §7.1/§7.2* — **PASS with a hard requirement:** opacity
may hide the bottom and may **never** hide a traversal affordance. The stepping stones stay
legible from above at every phase of the cycle. A player who falls in fell for a reason they
can name.

**Spike gate — no spike claimed.** §4.4 caps a session at roughly one and this is emphatically
not it: conditions 1 (stake) and 3 (simultaneity) are absent by construction, and condition 4
(legibility) fails **on purpose** — §4.2's Phasmophobia test is failed knowingly, because this
beat is atmosphere and admits it rather than being dressed up as a moment that travels.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a and structurally compliant; nothing is
exposed at all. *8.2 startle as build* — not violated, and the constraint above is what keeps it
that way permanently. *8.3 reliable telegraphs* — n/a; the murk precedes nothing, so it cannot
teach anyone when they are safe. *8.4 scripted set-pieces* — not violated; a standing material
condition fires nothing. *8.5 illegible chaos* — **the live risk**, answered by §6.0's
constraint: depth must read as depth, never as noise. *8.6 unrelenting tension* — not violated
and structurally healthy; it is one surface in a bright level and the player may simply walk
away. *8.7 permanent safety* — n/a; nothing here is a sanctuary. *8.8 streamer-first* — not
violated; see the spike gate, where the un-clippability is owned rather than engineered around.
*8.9 punitive failure* — **PASS and load-bearing:** falling in costs a wet walk back and nothing
else. **This direction does not authorise a drowning rule in this level**, and if one is ever
added the lake must be re-directed rather than inheriting this entry.

**Handed off, unanswered — implementation, not direction:** extinction distance, silt density
and scatter, the green-brown's actual chroma, the night multiplier, the turbidity veil's
strength, and whether the bed is ever visible at the shoreline (all BT-9's, as the builder, and
none of them the director's to pick); whether this level has any water-death rule at all
(`MECHANICS-BIBLE.md` and canon — **not this file's**, and the §8.9 line above is written on the
assumption it has none); and whether the stepping stones need their own legibility treatment at
night beyond the level's emissive aids (`/spec-level` plus the builder, with §7.1's requirement
above as the acceptance test).

---

**The green crossing, RE-DIRECTED 2026-08-29 (STONE-2) — because a drowning rule was added to this
water and the entry above forbade inheriting itself.** Altitude: **scene**, the same 12 m crossing,
with the water-death system named as its subject. This is not a new direction; it is the
re-direction the 2026-08-28 entry demanded in writing and did not get.

**What happened, in order, and why it is a process finding and not only a geometry one.** The
entry above closes its prohibition sweep with: *"§8.9 punitive failure — **PASS and load-bearing:**
falling in costs a wet walk back and nothing else. **This direction does not authorise a drowning
rule in this level**, and if one is ever added the lake must be re-directed rather than inheriting
this entry."* On **2026-08-29 WATER-3 added `WaterGeometry.DrownAfterSec`** and pointed this lake at
it. No re-direction was run. **STONE-1 then measured what the inherited entry had become:** falling
off Stone6 or Stone7 drowned on **every one of 16 headings in every load case**, and a body left
standing still on the island's shelf slid down a 57.6° flank and drowned unaided in 3.1–3.3 s. The
§8.9 line was not merely stale; it had inverted. **The lesson to keep is the shape of it: a
directed entry that names its own repeal condition still needs somebody to notice the condition
firing, and nobody did for a day.**

**The five.** *Belief:* the one this level teaches and the entry above authored — every surface here
answers a question, and the water is the wet gap between stones; the worst it can do is make you
walk back. *Break:* the instant that stops being true. *Why here:* **it does not have to break
here, and that is the finding.** WATER-3 installed the break with no direction behind it, and
STONE-1 measured the version the level actually shipped: a break that fires on a sideways step,
with no counterplay on any heading. Canon fact 7 — *"never cheap: the player who gets got could
always name what they ignored"* — is the exact test that fails, and §7.1's *avoidable but barely*
is the shape it should have had. **STONE-2 does not remove the break. It moves it off the edge of
the stone and into the middle of the water**, where reaching it takes a decision: swim out past
both shores, or stay out there too long. *Who perceives it:* unchanged and deliberately **not**
simultaneous — the crossing is single-file, so the faller is alone and the group watches from the
bank (§5.4 asymmetry, for free). *What changes afterward:* the water acquires a **middle**. Before,
the whole lake was lethal and the stones were the only ground; now the lake has a shallow rim, a
beach at the island, and a deep band between them that is the only part that kills. The stones stop
being the difference between alive and dead and become the difference between dry and wet — which
is what the entry above assumed all along.

| Device | Status in this level | Note |
|---|---|---|
| The unreadable dark | **still a deposit — NOT spent, and the constraint is re-affirmed verbatim** | Nothing is shown in or on this water: no shape, no breach, no wake, no silhouette, no uncaused ripple. The re-grade adds no event of any kind; it moves a contour. The deposit is untouched and the camp lake's is untouched with it. |
| Colour temperature as threat channel | **not re-spent** | The grade is BT-9's and STONE-2 changed no material. |
| §8.9's "falling in costs a wet walk back" | **CORRECTED, and this is the one real change to the block above** | It is no longer true and has not been true since WATER-3: falling in the deep band is a death with a respawn. What STONE-2 restores is the *fairness* half, not the old harmlessness — measured, 320 driven landings off five stones × 16 headings × four load cases (worst = carrying at `speedFactor` 0.75 **and** `Soaked`) now escape, against **144 of 320** that drowned before — 42 of 80 in that same worst case. Read the old §8.9 line as superseded by this row, not as still standing. |
| No new device row opened | — | The temptation was a row for *the safe middle that is not the middle* (a body of water whose danger is a distance from either shore rather than a depth). **Refused as register inflation**, checked against six rows: *The unreadable dark* (ambiguity by opacity — this is a distance, and it is legible); *Refuge maintained only by leaving it* (that row's refuge degrades while you sit in it — the beach does not); *Route loss behind the group* (nothing closes); *Non-predictive threshold* (the water forecasts nothing and never did); *Landform as disorientation* (the ground here does exactly what it looks like); *The blind hide* (no perception is traded). **The honest reading is that this pass spends nothing at all — it removes an undirected death and leaves a directed one.** A fairness correction is not a device. |

**Dread gate.** *Ambiguity §2.1* — **PASS**, unchanged and untouched; the murk still resolves
nothing. *The clock §2.2* — **FAIL**, unchanged and correct: the Bubble Test has no pressure
driver and must not grow one. The 3.0 s drowning timer is **not** a clock in §2.2's sense — it is
per-player, self-initiated, invisible to everyone else, and it starts only once you are already in
the water. Do not let a future pass log it as one. *Break §3.1* — **n/a**; there is still no build.
*Incompleteness §3.2* — **n/a**. *Degradation §3.4* — **n/a**; the beach is not a sanctuary and
nothing recovers on a window. *Affect §6.0* — **PASS, with a NEW conditional the builder owns:** the
beach must read as a beach *from a stone*, because the whole fairness claim is that a faller can see
where to go. Photographed: `docs/qa/2026-08-29-STONE-2/island-shore-day/Stone2-6s.png` and
`docs/qa/2026-08-29-STONE-2/beach-section-day/Stone2-6s.png`. If a later murk pass hides the shelf,
the geometry stays fair and the *read* stops being, and that is a §6 defect, not a §8 one.
*Fairness §7.1/§7.2* — **PASS, and this is the gate the pass exists to flip.** Before: no heading
off two of the eight stones survived, in any load case — not "avoidable but barely", unavoidable.
After: every landing off every stone escapes in the worst realistic load, and the thinnest measured
margin is 0.017 s on one landing whose direct line is blocked by the next stone. §7.2 also gains
something: the rule is now *learnable in one attempt* — shallow at both shores, deep in between —
where before the rule was "the lake kills you" with a shelf that looked safe and was not.

**Spike gate — no spike claimed**, and the reasons are the entry above's, unchanged: no stake the
group knew about, no simultaneity, and §4.2's legibility failed knowingly.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; nothing is exposed. *8.2 startle as build*
— not violated; nothing fires. *8.3 reliable telegraphs* — not violated, and slightly improved: the
shallow rim is now an honest, always-true cue about where the water is safe, which is §8.3's
*fairness contract* channel (`LEVEL-BIBLE.md` §8.2) and not a dread telegraph. *8.4 scripted
set-pieces* — not violated; a bed contour fires nothing. *8.5 illegible chaos* — not violated, and
this is where the pass helps: a death with no available counterplay on any of 16 headings is
illegible by §4.1 condition 4, and it was the shipped state. *8.6 unrelenting tension* — not
violated. *8.7 permanent safety* — **the one prohibition a reader will suspect this pass of, and it
holds:** the island beach is not a sanctuary, nothing about it degrades or needs to, and the deep
band still kills a body parked in it in 3.0 s (measured, and kept in the run as a positive control).
Making a fall survivable is not making a place safe. *8.8 streamer-first* — not violated. *8.9
punitive failure* — **PASS, re-argued rather than inherited:** drowning costs a death and a respawn,
which canon (2026-08-21, comic drowning; PLAYTEST-2, respawn not fainting) puts in bounds; what
§8.9 actually forbids is a cost that inverts the release valve, and *"we almost died"* only becomes
*"we wasted an hour"* if the loss is large or the death was unearned. STONE-2 addresses the second.

**Handed off, unanswered — implementation and ownership, not direction:** whether the crossing's
stone SPACING should change now that MOVE-8's jog-tap range (0.887 m) sits under two of its edge
gaps (0.900 and 0.917 m) — that is a /direct requirement-2 question about the spacing *pattern* and
a `/spec-level` job, not a bed question, and STONE-2 waived the two gaps by name rather than answer
it; whether the murk's near-field should be re-graded now that a shallow beach exists to be seen
through (BT-9's); and every number in the water contract — `DrownAfterSec` is Talon's and was not
touched.

---

**What a death DOES between the kill and the respawn — directed 2026-08-30 (W7-4), and it is a
SYSTEM entry, not a scene one.** Altitude: **system** — "a clock, a death, a light, a carry: what
feeling the system manufactures at all". It is filed here, under the green crossing, because that
is where Talon met it; it binds every `RespawnCause` in every level, and a later director must not
read it as the lake's.

**The trigger.** Talon, playing the WAVE-5 build, note 6, verbatim: *"When the player 'drowns', the
body floats and spirals upward, to the surface, into the sky, it looks like a tornado took them up.
Please address this."* Measured behind the words: the beat lofted the rendered body 4.196 m on a
sine arc and spun it 4.487 whole turns in two seconds, over a body the water contract was already
holding 1.25 m under the surface.

**§4.3 names this failure in its own words, and that is the finding.** *"Surprising but
retroactively explicable... 'The causeway flooded behind us while we argued' is a story. **'We were
teleported into the sky' is a bug.**"* The shipped beat produced, on a real playtester, almost that
exact sentence. It is the first time anything in this repo has failed a Part I clause by
reproducing its example nearly verbatim, and it is worth keeping for that reason: the doctrine was
right and nobody had run the check.

**The five.** *Belief:* my body obeys the world's rules — it falls, it floats, it stays where
physics puts it. *Break:* the instant it stops obeying and starts being MOVED. *Why here:* it does
not have to break here, and that is the whole diagnosis — the break was never directed, it was a
flourish authored to read "comic" from across the map and never checked against the one place a
body is already suspended in a fluid. *Who perceives it:* everyone, which is what made it urgent
— the beat is replicated and it is the body other players watch. *What changes afterward:*
nothing did, and nothing should. A death here costs a walk back; the register is the only thing at
stake.

**The direction, in one line: a death must ARRIVE somewhere, and the direction it arrives in is
never up.** Three constraints, all constitutive:

1. **It resolves.** The body reaches a terminal pose and holds it until the respawn takes it. An
   excursion that merely returns to rest reads as *something happening to* the body; a pose reads
   as *the body being dead*. This is also what fixed the "into the sky" half mechanically: the old
   arc's descent was real in the code and was never once on screen, because it returned to rest on
   the same frame the respawn teleported the body away.
2. **It never trends upward.** Up is the direction of being *taken*, and the register law is
   absolute that nobody is ever taken. This is the one place the register law and §4.3 want the
   same thing for different reasons, and it is not negotiable by a later tuning pass.
3. **The comedy lives in the POSE, not the trajectory.** §9's payoff currency is excess and
   wrongness, and a pose can be excessive while standing still — a body that lands slightly wrong
   is funnier than a body spinning four and a half times, and it survives being clipped out of
   context, which four and a half revolutions does not. **Trajectory excess is what produced the
   tornado.** Direct excess into the shape a body ends up in.

**Per cause — they should differ, and the rule is one line: the beat never fights the direction
the world already gave the body.** A drowning goes down, because that is what water does with what
it takes and because it is the most legible possible read of the cause (§4.1 condition 4, applied
to a death: a teammate on the bank can see *that they drowned* and not merely *that they died*). A
fall off the edge of the world and a fall out of it are already going down; the beat adds a topple
and no lift beyond the clearance a flat body needs. No cause is given an upward term.

| Device | Status in this level | Note |
|---|---|---|
| No new device row opened | — | The temptation was a row for *the death that resolves* (a failure state that ends in a held pose rather than an excursion). **Refused as register inflation**, and the argument is STONE-2's verbatim: a beat reading as what it is, is baseline legibility, not a technique that produces dread or a spike. Checked against five rows and all five miss: *The unreadable dark* (nothing is withheld here — the read is the point); *Wrong silence* (no bed is spent, nothing is withdrawn); *Doorway as proscenium* (nothing is framed); *Accumulating wrongnesses* (this REMOVES a wrongness, and the wrongness was unintentional, which is the opposite of that row's device); *Per-player sensory asymmetry* (the beat is identical on every peer, deliberately). **This pass spends nothing. It stops a beat from spending the player's trust by accident.** |
| The unreadable dark | **untouched, and one consequence noted for the builder** | A body that sinks into BT-9's murk is invisible within a second, which is correct for the murk and worth stating rather than discovering later: the drowned body is not a *sight* any more, and the death's legibility now rests on the nameplate going and the player being gone. That is enough for a harness level with no group in it. It is a thing to re-check the first time two people are in this water at once, and it is the fork in §13. |
| §8.9's cost | **unchanged** | Nothing here changes what a death costs. Talon's 2026-08-21 repeal stands, the respawn stands, the walk back stands. |

**Dread gate.** *Ambiguity §2.1* — **n/a**; a death beat is a release, not a build, and it spends
nothing. *The clock §2.2* — **n/a**, and the Bubble Test must still never grow one. *Break §3.1*
— **n/a**; there is no build in this level to break. *Incompleteness §3.2* — **n/a**.
*Degradation §3.4* — **n/a**. *Affect §6.0* — **the gate this entry exists to flip, and it was
FAILING.** The beat was perceivable and landed as confusion; a player's honest description of it
was a bug report. It now lands as a body dying, which is a small feeling and the correct one for a
movement harness. *Fairness §7.1/§7.2* — **untouched**; W7-4 changed nothing about when a death
fires, and STONE-2's measured escape margins stand unaltered.

**Spike gate — no spike claimed, and this is worth saying out loud because the old beat was
reaching for one.** Stake: none the group knew about. Simultaneity: the faller is alone.
Legibility: §4.2 failed knowingly. **A death in a harness level is not a spike and must not be
dressed as one** — §4.4's ceiling is one per session and inflation devalues every one of them.
Four and a half revolutions and a 4.2 m loft is a spike's costume on a beat with none of a spike's
four conditions, which is precisely why it read as arbitrary.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; nothing is exposed. *8.2 startle as build*
— **improved**: the old beat's launch was a startle-shaped event used as the whole content of a
non-event. *8.3 reliable telegraphs* — n/a; a death telegraphs nothing. *8.4 scripted set-pieces*
— **the one a reader will suspect, and it holds:** the beat does fire identically every time, but
it is a *state transition's presentation*, not an authored dramatic moment; §8.4 forbids
possibility-space-replacing set-pieces, and a consistent way of showing a state is §7.2's
learnability, which the same file requires. *8.5 illegible chaos* — **the prohibition this pass
actually enforces.** *8.6 unrelenting tension* — n/a. *8.7 permanent safety* — untouched. *8.8
streamer-first* — **and this is the sharp one.** The old beat was authored to "read as a joke from
across the map", i.e. for the clip; §8.8 says a moment engineered for the camera reads as
inauthentic. The new beat is directed at the player standing on the bank. That it also survives
being clipped is the byproduct §8.8 says it should be. *8.9 punitive failure* — unchanged.

**Handed off, unanswered — implementation, and not the director's:** every number in the curve
(how far a body sinks, how long the hold is, the settle easing) is the builder's and is tuning, not
direction; whether the drowned body should make a *sound* as it goes, and whether the sputter
bubbles at the surface are the right last thing a watcher sees, is `vfx-audio-sync`'s and the
`sound-*` family's and nothing here pre-empts it; and whether the avatar should have a bespoke dead
*pose* in the rig rather than a rotated standing one is W7-3's, which owns the mesh and the clips.
**The Bubble Test's green crossing — directed 2026-08-28 (BT-2: the murky lake, the stepping
stones and the Postpile island, in the instrument level `scenes/game/world/bubbletest/`).**
Altitude: **scene** — one 12 m crossing inside one section. The level altitude is deliberately
not claimed: the Bubble Test has no session arc to direct, because it is a movement instrument
(six colour-coded slabs with their names on billboards over them), and directing an arc onto a
rig would be inventing a feeling the artefact cannot hold.

**The one finding that matters, stated before anything else: this level is not a horror level
and must not be directed as one.** Six of the seven dread-gate items fail here, and every one
of those failures is correct. There is no clock and the Bubble Test must never author one
(§2.2); reaching the island is a complete all-clear (§3.2 fails); the island is permanently
safe (§8.7 fails); nothing builds, so nothing breaks (§3.1 fails). The affect that *is*
available without an entity, a clock or a threat is **§1 creepiness in its narrow sense —
ambiguity about how to read a thing** — and exactly one thing in this section supplies it for
free: **the water does not tell you how deep it is.** You can see every stone; you cannot see
the bottom. That is the whole affect budget of this section and it is enough for the one
feeling a movement test can honestly produce, which is **hesitation at the moment of
committing**.

The direction in one line: **you can read the whole crossing before you step, and you cannot
read the one thing that would tell you what stepping wrong costs.**

**The apparent artefact is QUARANTINED here, not spent — and this is the load-bearing register
ruling of the pass.** The §10 row's device is that *the world produces one piece of apparent
craftsmanship in a game whose every other structure is a camp building the players can name*.
The Bubble Test names everything: the sections wear floating labels, the palette is a legend,
and a player standing on the pavement is standing inside a manifestly authored rig. **The
context that makes the device work is absent, so the device cannot fire and cannot be burned.**
Two consequences are binding, not tuning. **(a) BT-2's postpile is set dressing on a test rig
and must never be logged as the device's first build** — the camp pocket's first appearance is
still unspent and still owed its own approach, interior and exit. **(b) The goal paint stays in
this level.** §4 rule 7 puts `green_goal.tres` on the tallest column top; in the camp that is
constraint (b)'s "one deliberate tile" and it would kill the device outright. The Bubble Test's
column geometry may be lifted into the camp pocket only stripped of it.

| Device | Status in this level | Note |
|---|---|---|
| **The apparent artefact** | **QUARANTINED — not spent, not burned** | See above. The row's constraint (c) *"it may not be useful"* is violated here by construction — the island is the destination of a crossing and holds bubbles — which is a second, independent reason the spend cannot count. A useful pavement is a base; that is the exact §8.7 failure the row exists to prevent, and it is tolerable here only because nothing in this level is at risk. |
| **The bounded exception** | **not spent** | Same quarantine, same reason: a room with a sharp edge violates a world-model the connective woods teach, and this level teaches no world-model to violate. Its two level-altitude spends (savanna, postpile) stand unchanged; this is not a third. |
| Colour temperature as threat channel | **untouched — ninth occurrence of the same trap** | The lake wants to go blue and cool is the warning direction. **Requirement: the murk reads darker and lower-contrast than the pale-green bank, never bluer.** The section's whole palette job is a pale green tint (§4 rule 7); the lake is the one dark value in it, and it earns that by value, not by hue. Handed to BT-9 as a constraint on `lake_murk.tres`, not as a colour. |
| The unreadable dark | **deposit posture, NOT spent — sixth surface** | The murk is opacity, and opacity with nothing behind it is the deposit the lake row already describes. **Nothing is ever in this water.** No shape on the bed, no silhouette under the surface, no object to find by falling in. Emptiness under an opaque surface is the entire charge; one thing down there converts creepiness into a mechanic, and BT-11 must not read the "5 bubbles on or around the stones" allocation as licence to sink one. |
| Wrong silence | **REFUSED — ninth occurrence** | Do not wire `AmbientBed.Withdraw()` to submersion, to the shore, or to the section volume. Going under water is the most explicable absence there is (the lake entry's own words) and the section boundary is a footstep-material change (the postpile entry's own words). This section hosts both failure modes at once. |
| Day/night cycle as clock | **borrowed, not re-spent** | The Bubble Test rides `RunDriver`'s shipped 720 s period and authors nothing. The shipped swim chill is **not** a clock and must not be built as one — per-player, invisible to others, self-initiated, reversible by turning around. The lake entry's ruling transfers verbatim. |
| Night reversal | **leaned on, not resolved** | `MinAmbientEnergy` untouched; the §6.2 fork is not moved. D10's emissive goal edges are the level's own legibility rule, authored by the program, not a directed beat — recorded so nobody counts them as this row's substance. |
| Fog as sightline denial | **not spent, and not available** | The row's reuse rule requires something approaching. Nothing approaches. |
| Landform as disorientation | **not spent — inverted, deliberately** | You can see both banks from every stone and the island from the shore. That inversion is §7.1 and §8.5 doing their job in a test level: a crossing whose route you cannot read is illegible chaos, not dread. |
| Scale that is not the player's | **not spent** | The column tops are sized to be stood on. Same inversion the postpile entry records, for a duller reason: here it is a floor because the packet needs walkable tops, not because the world is making a claim. |
| Cost-to-know instrument | **not spent, and the honest statement of this section's ceiling** | §2.3 trades safety for *information*. The murk withholds information and offers no trade to get it — you can enter the water, and the water still tells you nothing, because there is nothing to learn. Same diagnosis the lake pass recorded, and the reason this section tops out at hesitation rather than dread. |
| Per-player sensory asymmetry | **produced, not spent** | The swimmer's eye sits at the waterline and cannot see over the water (shipped contract); the player on the bank sees a head and the whole route. Falls out free; no machinery. |
| Accumulating wrongnesses · Persistent scarring · A world that breathes · Doorway as proscenium · Stairs that lie · Non-predictive threshold · Route loss · Sound with no visible source · Synced audio-visual peak · Flash beacon · Forced separation · Teammate voice cut-out · Contagious panic · The blind hide · Attention as a resource · Authority withdrawn · Position without judgment · The watched · Refuge maintained only by leaving it · Open sanctuary inverted by dark · The ration that never refills · Rising tide · Proximity voice falloff · Tension-budget pullback · Canopy as spatial darkness · Glow-stick trail | **untouched** | None is placed, leaned on or implied. The section places no creature, authors no clock, withdraws no sound and grants no capability. |

**The four composition requirements the geometry owes, and why each is affect and not taste.**
They constrain shape; none names a value, which stays the builder's.

1. **The whole route is legible from the west bank before the first step.** Every stone and the
   island in one look. §7.1 (avoidable but barely) and §8.5 (illegible chaos is tedium): in a
   level whose entire purpose is testing whether the movement reads, hiding the route measures
   nothing and frustrates.
2. **The gaps are generous at both ends and tightest in the middle.** §3.5 is rhythm, and at
   12 m the rhythm's only expressible statement is where the hard beat sits. Put it where both
   banks are equally far — the moment of maximum commitment is the moment of maximum care. The
   corollary is the one that protects §8.9: **the last gap is never the hardest**, so the sting
   is never "I fell in with the island in reach."
3. **The line is not straight.** A straight run of stones is one decision solved from the bank; a
   drifting line is eight decisions, each requiring a re-aim. This is the only thing in the
   section that makes the crossing a *skill* rather than a corridor, and it is free.
4. **Falling in is recoverable from everywhere, and the island is reachable only by the stones.**
   §7.4's anti-unwinnable guarantee has no valid exception: the basin's outer slope is a walkable
   ramp all the way round, so the water never traps. The island's own wall is steep enough that
   you cannot climb out of the water onto it — which is what keeps the stones the answer — and
   that is a placement fact, never a capability granted to anything.

**Dread gate.** *Ambiguity* (§2.1) — **pass, narrowly**, and it is the section's only pass: the
murk is genuine unresolvable ambiguity about depth, and nothing in the section resolves it, so
the spend is zero. *The clock* (§2.2) — **fail. This is atmosphere, not dread, in those words.**
The Bubble Test has no pressure driver and must not grow one. *Break* (§3.1) — **fail**; there
is no build to break, only a micro-arc whose peak is the landing. *Incompleteness* (§3.2) —
**fail**; reaching the island is a total all-clear. *Degradation* (§3.4) — **fail**; the island
is permanently safe, §8.7, tolerable only because nothing is at risk anywhere in this level.
*Affect* (§6.0) — **pass, conditional on BT-9**: if `lake_murk.tres` is transparent enough to see
the bed, the composition delivers nothing and the conditional has failed silently. That is a
legibility-to-affect dependency and it is named here so BT-13 can check it rather than assume it.
*Fairness* (§7.1, §7.2) — **pass**: the route is visible, the gaps are held under the *measured*
jog-tap range, and every failure is a swim and a walk.

**Spike gate — no spike claimed, and none is available.** Condition 1 (stake) fails outright:
nothing in this level is on the line. §4.4 caps a session at roughly one spike and a rig should
produce zero; manufacturing one here would inflate the currency for the levels that need it.

**Prohibitions, all nine.** §8.1 over-exposure — nothing is exposed; no threat exists. §8.2
startle as build — none; no cue in this section is timed to surprise. §8.3 reliable telegraphs —
none authored; D10's night edges are a *fairness* cue about state (`LEVEL-BIBLE.md` §8.2's honest
lane), not a dread hint, and the two must stay different channels. §8.4 scripted set-pieces —
none; the crossing is possibility space and the drama is whichever friend falls in. §8.5
illegible chaos — actively guarded by requirement 1. §8.6 unrelenting tension — no tension is
authored to be unrelenting. §8.7 permanent safety — **violated, knowingly**: the island never
degrades. Recorded rather than waived, because it is the exact hazard the postpile's own entry
flags, and it is the second reason the artefact device stays quarantined. §8.8 streamer-first —
nothing here is staged for a camera; the captures D11 requires are documentation of geometry.
§8.9 punitive failure — guarded by requirement 2 and by the shipped contract: the cost of falling
is time, never a run.

**Canon updated:** this entry (new, §12). **No device row in §10 was changed, and that is
deliberate** — the apparent artefact and the bounded exception both stay at the states the
2026-08-13 postpile pass left them, because a quarantine is not a spend and recording it as one
would burn a device the camp still needs whole. No fork opened, none resolved; §6.2 is not moved.

**Handed off, unanswered.** How opaque the murk actually is, and whether it holds at the grazing
angles the shore is shot from (BT-9, `lake_murk.tres` — with this pass's one binding constraint:
darker and flatter, never bluer). Whether the water plane needs any surface motion at all, and
whether stillness or motion better sells "you cannot see in" (BT-9 / `vfx-*`). Whether the five
green bubbles sit on the stones, over the water, or on the island, and the ruling above that
**none may sit under the surface** (BT-11). Whether the section's footfall changes on stone
(`sound-soundscape-construction`; unbuilt here, and the postpile entry already owns the lane).
Whether this level ever wants an ambient bed at all — and the standing refusal above if it gets
one (BT-12/BT-13). Every distance, height, gap, radius and count in the four requirements
(BT-2's builder; the director names no value).
---

**The TV that is a doorway — directed 2026-08-28 (BT-10, the Bubble Test level).** Altitude:
**scene** — one moment with a setup, a break and a tail, inside a level that is a
movement/networking harness rather than a directed level. A `level` pass is flagged for
nothing, because Bubble Test authors no arc and should not: directing a test harness into a
session would make it a worse harness.

**The honest state first, because it decides everything below.** This beat **cannot produce
dread and must not be graded as if it could.** §2.2 is unambiguous — dread is ambiguity under
a *certain, visible, shared, inevitable* clock, and this level has no clock, no threat and no
pressure of any kind. What is available today is **creepiness** (§1): ambiguity about the
presence of threat, which costs nothing, needs no entity, and survives being in a group. That
is also exactly what Talon asked for — *"a bit creepy/disorienting, the 'not sure what just
happened' feeling"* — so the achievable target and the requested one are the same target, and
the direction is built for it rather than reaching for a dread it has no instrument to make.

The direction in one line: **the world declines to acknowledge that anything happened, and the
player is the only evidence.**

**The five.** *Belief:* the TV is scenery — this level is boxes and bubbles and every object
in it is inert. *Break:* the screen does not stop them. *Why here:* it has to be somewhere the
player went for another reason, which is the only thing that makes the find theirs (§4.5); a
TV placed as a landmark is a quest marker and forecasts itself. *Who perceives:* **one player,
deliberately** — see the spike gate. *What is true afterward:* nothing, and that is the beat.
They come back to the spawn ring, the TV they walked into is still across the plaza still
playing static, and no system anywhere reacts. §2.1's all-clear is withheld at zero build cost.

**The four decisions the packet asked for.**

1. **What withholds on arrival: every cue, without exception.** No sting, no drone, no arrival
   sound. The crackle belongs to the *transition* — it is the sound of the television, and it
   is finished before the room exists. No motion: nothing in the room may animate on entry.
   And the one that will be violated by copying the section-file pattern — **the TV room gets
   no `SectionLabel`.** Every other section in `BubbleTest.tscn` carries a floating Label3D
   naming it. A name is an all-clear: it tells the player the game meant this, and being told
   the game meant it is precisely the resolution that spends the fuel. Dev instrumentation
   (the `[bubbletest.section]` console print) is not a player-facing cue and is fine.
2. **Flash duration: refused, per §0 — the director declares what must be true, never how
   much of it.** What must be true: the flash must **end before the room is legible**. If the
   player watches it fade, the flash becomes the event and the room becomes its aftermath;
   this direction requires the reverse — the room is the event and the flash is a thing they
   are not certain happened. Floor: long enough that the geometry swap is never seen. Ceiling:
   short enough that it never reads as a *transition animation*, which would announce a
   designed passage. The ported 0.35 s satisfies that shape and is neither blessed nor refused
   here; the number is a playtest call and it is Talon's after he has stood in it.
3. **Does the lamp flicker? No — and this is the pass's most important refusal.** A flicker is
   §8.2's startle shape used as a build, it becomes a §8.3 reliable telegraph the second time
   it happens, and it is an animate thing in a room whose entire charge is that nothing is
   animate. It is also the *wrong register*: a flicker is wrongness-by-disorder, the cheap
   half, and the reference register (mp-foundation's `ElsewhereRoom`, read not ported) is
   wrongness-by-too-much-order. The lamp burns steady. **The room's whole animation budget is
   the return TV's static** — which was already moving before the player arrived and keeps
   moving after they leave, because it is not reacting to them.
4. **Does the return TV face you? No — you must turn to find it.** The way back is *found*,
   not handed over (§4.5), and the disorientation Talon asked for is spatial: it requires
   having been oriented and then not being. Arriving facing the room's content and having to
   turn away from it to leave is the cheapest honest version of that. **Hard constraint, and
   it is a fairness one (§7.1) rather than a taste one: the return TV must be unmissable the
   moment the player turns, with no search.** Turning is the cost; hunting is not. This is a
   sealed room 40 m down, `VoidKillY` is −70 and the off-map radius is 0 there, so nothing
   rescues a player who cannot find the exit — a hidden way out is a stuck player, and §7.4
   admits no atmospheric exception.

**A fifth ruling the packet did not ask for, because its own tone reference contradicts the
geometry it ports.** The Den's frames are *crooked*; `ElsewhereRoom`'s are perfectly straight
and perfectly empty, and that room's own doc says it is "the exact inverse of the den's crooked
ones." They are a matched pair. Sail is porting the den, so **Sail gets the den's half: keep
the crookedness exactly, and do not import the other half's register on top of it** — the
result would be neither. Second half of the same ruling: **the frames stay empty or
near-empty.** A picture in a frame is exposition (§6.5), and this room must explain nothing
about who lived here. Crooked and empty is a wrongness; crooked with a family photograph is a
story, and a story resolves.

**Dread gate — run honestly, mostly failing, which is the correct output.**

| Item | § | Result |
|---|---|---|
| Ambiguity | 2.1 | **Pass**, and it is the only thing this beat runs on. Nothing resolves; the all-clear is withheld structurally (nothing in the game can acknowledge the trip because nothing is watching for it). The spend is accounted: one prop's inertness. |
| The clock | 2.2 | **FAIL, and unfixable here.** No pressure driver, no approach, no inevitability. This is atmosphere and creepiness, not dread, and the word is used deliberately. Bubble Test must not grow a clock to fix this — a harness with a pressure driver is a worse harness. |
| Break | 3.1 | **Pass.** The build is one sentence long ("this is scenery") and it breaks completely. |
| Incompleteness | 3.2 | **Pass by structure.** The local tension resolves (you got out); the larger one — that the level contains a place with no geography — is left standing and is never addressed. |
| Degradation | 3.4 | **N/A, stated.** There is no sanctuary here to degrade and no session arc to degrade it across. |
| Affect | 6.0 | **Pass, conditionally** — on decisions 1, 3 and 4 above holding. It fails the moment a label, a sting or a flicker tells the player what to feel. |
| Fairness | 7.1, 7.2 | **Pass, on the decision-4 constraint.** The rule is learnable in one use (walk into the screen, you go; walk into the other screen, you come back) and consistent. It fails outright if the exit can be missed. |

**Spike gate: no spike claimed — and this beat must never be promoted into one.** §4.3 is the
reason and it is worth stating because it is counter-intuitive: *"we were teleported into the
sky" is a bug, not a story.* Condition 4 (legibility) fails permanently for any watcher — a
peer sees a teammate vanish off flat ground with no visible cause, and there is no clip in
which a stranger could tell what happened. Condition 3 (simultaneity) is failed **on purpose**:
the transition is local to the traveller. That is not merely a netcode convenience, it is the
direction — if everybody flashed, the vanish would become an announced event and the watcher
would be handed an explanation they did not earn. One player perceives; the others get §5.4's
asymmetry (a friend was there and now is not, and comes back with a claim about a couch), which
falls out free and is the funnier and better half.

**Prohibitions — all nine, named.** **8.1 over-exposure:** nothing is exposed; there is no
threat here and the room must never acquire one. **8.2 startle as build:** the refused lamp
flicker was this prohibition in disguise; the flash is a *release* and lasts less than a
second. **8.3 reliable telegraphs:** the TV forecasts nothing about anything else in the level,
and nothing in the level forecasts the TV — the hidden set must not be marked, lit or pathed
toward. **8.4 scripted set-pieces:** this fires identically every time, which is a real cost
honestly borne — the mitigation is that it is *one prop in a harness*, not a beat the level's
pacing rests on, and it must not be repeated a third time in this world. **8.5 illegible
chaos:** the room is small, ordered and reconstructible; the player can always point at the TV
they walked into. **8.6 unrelenting tension:** nothing sustains here. **8.7 permanent safety:**
N/A — the room is neither a sanctuary nor sold as one, and no mechanic makes it safer than the
surface. **8.8 streamer-first:** explicitly refused above — the beat is designed for the one
person in the room and is deliberately un-clippable. **8.9 punitive failure:** nothing is lost
by finding it or by missing it.

**Register law (canon).** Nothing gory, nobody taken, nobody held. A couch, a lamp, seven
crooked empty frames and two televisions, in a room you may leave the instant you turn around.
Survives an out-of-context clip with room to spare.

**Anti-inflation check on the new §10 row, run before it was added.** Five rows are adjacent
and none covers it. *Doorway as proscenium* is a framed view **held** through an opening — the
charge is what you see in the frame, and here you see nothing because you are already through;
it stays reserved for EPIC 2. *Stairs/doors that lie* is a route that misreports **length or
destination** — the TV was never read as a route, so there is no such claim to break (the row
is amended with this near-miss, not spent). *Interior larger than its shell* was **REFUSED**
once already on the inoculation finding, and does not apply anyway: this is a genuine
relocation 40 m down, not a shell contradicted by its contents. *Non-predictive threshold* is
the closest live entry — §8.3 applied to space — but it requires the player to **recognise a
threshold** and get no forecast from it; here there is no recognised threshold at all, which
is a different mechanism reaching a similar place. *The bounded exception* is one landscape
**law** enforced in a room with an edge; the TV room enforces no law, it is simply a place
that is nowhere.

**Two constraints handed to placement, which the director gives as requirements and not as
coordinates.** (a) The **hidden** TV must sit where a player arrives for another reason, and
must not be visible from the hub — a found thing is §6.4's wrongness, a seen thing is a
waypoint. (b) The **return destination** must put the player where the hub TV is visible but
not adjacent, because the world's refusal to react is only legible if the unchanged prop is in
shot when they get back. Both fall out of the shipped layout for free; neither needs geometry
invented.

**Canon updated:** this entry (new); one new §10 row (*The object that is a door*, with its
decay ruled as `spent-here` in a harness and available game-wide); two §10 rows amended and
re-refused (*Doorway as proscenium*, *Stairs/doors that lie about length or destination*); one
new §13 fork. **No fork resolved.** §6.2 is not moved — this beat is underground, authors no
darkness, reads no ambient floor and leans on neither side. §9's ratio is **flagged, not
resolved**: this beat has no absurd payoff and the director declines to invent one, per §9's
standing instruction to build sincere first. §11 is untouched — nothing has landed in a
session.

**Handed off, unanswered — none of it is this file's.** Every duration, distance and light
value (playtest, and the director does not pick), the flash time above included; the crackle's
place in the mix and whether an underground room needs any bus treatment at all
(`sound-*`, entry point `sound-integration-guide` — noting that this room has **no ambient
bed** and must not grow one to fill the silence, because the silence is the direction); how the
static reads at a distance without becoming a beacon (`vfx-*`, with prohibition 8.3 as the
acceptance test); the teleport's epoch and cooldown semantics (`MECHANICS-BIBLE.md`, the
implementer's); whether a walked-into screen needs any interaction feedback at all
(`INTERACTION-BIBLE.md`, the implementer's — the director's only constraint is that any such
feedback must not precede the transition, or it becomes a forecast); and **whether the game
proper may ever contain a place its map does not explain**, which is the new §13 fork below and
is Talon's on its trigger.

**The Bubble Test's value ladder, and what a self-lit surface is allowed to be — directed
2026-08-28 (packet BT-9b, the palette that separates in hue and not in value).** Altitude:
**system** — this is a rule about a class of surface, not a beat and not a place. Two questions
were put; the first is answered by refusing to re-open a standing entry, and only the second
produces anything new.

**Q1 — the lake. THE 2026-08-27 ENTRY STANDS UNCHANGED, AND THAT IS THE WHOLE ANSWER.** BT-2
filed the §6.0 conditional gate as FAILED. It measured `lake_murk.tres` at alpha 0.82 and read
the bed, the island wall and the full submerged shafts. That measurement was taken against
**BT-0's stand-in `StandardMaterial3D`**, not against BT-9's shipped `ShaderMaterial`, which had
not yet reached BT-2's base and has since landed. **A gate is not re-opened by a report about an
artefact that no longer exists**, so the director declines to re-direct it and the builder's job
reduces to verifying the shipped shader against the constraints already written. Verified on the
real GPU: the bed and the shafts are not readable at depth, the stepping stones read 22 L* above
the water they sit in (§7.1's hard requirement — opacity may hide the bottom and may never hide
a traversal affordance), and the surface carries a 9.08x luminance spread with **zero** pure-black
pixels, which is constraint (a) satisfied on its own terms: it denies depth without going flat,
so it reads as water and not as painted floor. **The conditional passes.** No number in that file
was changed by this pass and none should be — the packet's instruction to "raise opacity" was
written against BT-2's stale reading and is superseded by the measurement, not by an argument.

**Q2 — may an informational surface be exempt from the darkness? YES, AND HERE IS THE TEST.**
Programme criterion D10 says the darkness is real and only the named aids get to glow, so a
self-lit surface needs a licence rather than an excuse. The licence is checkable and it is the
one thing this pass adds to the file:

> **A self-lit surface may report the INSTRUMENT'S OWN STATE. It may never reveal the WORLD.**
> The test: *does switching it off change what a player can traverse?* If no, it is **signage**
> and it is exempt. If yes, it is an **aid** and it belongs to D10's budget.

A bubble counter and a section label pass — they report a score and a name, and a player who can
read neither can still cross every platform in the level. A lamp over the next jump fails. **The
exemption costs nothing here, and the reason is specific rather than convenient:** the 2026-08-28
entry above already ruled that this level is not a horror level and must not be directed as one,
six of seven dread-gate items failing correctly. There is no dread budget in the Bubble Test for
a scoreboard to spend. The lake's *unreadable dark* is a **deposit**, and a lit scoreboard does
not withdraw from it — nothing about a legible score tells anyone how deep the water is.

**The TV screen is exempt by a different route and it matters which.** It is not signage; it is
the tell for the §10 row *the object that is a door*, and a door nobody can find is not a door,
so its brightness is that row's own requirement rather than a favour. **One constraint, and it is
the load-bearing half:** the screen may be the brightest thing in the room and **must not become
the room's light source.** A screen that lights the room is a lamp, the room stops being dark,
and the §10 row's constraint (c) — nothing in the destination animates in response to the player
— is joined by a second failure nobody asked for. Emission, never a light node. Brightening it is
a **scale on the existing flicker curve and never a reshaping**: no new peak behaviour, so the
1% brownout stays a brownout and does not become a strobe (§8.2 — a startle is a sudden
*addition*, and scaling a curve introduces none).

| Device | Status in this level | Note |
|---|---|---|
| The unreadable dark | **deposit, untouched — NOT spent, and not withdrawn from** | Verified rather than re-directed. The W5 constraint travels intact: nothing is ever shown in or on this water. |
| Colour temperature as threat channel | **still `spent-here`, and the row's supporting sentence is AMENDED** | The 2026-08-27 entry justified this spend by saying the level's palette is "clean saturated toy colour **on off-white**". That is now factually stale: the geometry rung moved from off-white (L* 90.6) to a mid grey (L* 57.9). **The row's claim survives and the amendment is recorded so nobody re-derives it from a stale premise.** The lake's silt sits at L* 22.5, still the darkest and the only organically-graded surface in the level; the margin to the nearest geometry narrows from 68 L* to 35 L* and the *hue* exception — olive against a set whose neutral carries chroma 0.008 — is untouched. Do not spend this row again inside this level. |
| Self-lit signage (**new rule, no new row**) | **rule recorded, register NOT inflated** | Deliberately not opened as a §10 device. It produces no dread and no spike; it is a *permission boundary* on D10, and a register that grows a row for every permission stops being a list of techniques. §10 starts lying the moment it holds things that are not devices — the same refusal the 2026-08-27 lake pass made against a vertical-prospect row. |
| Night reversal | **not touched, not leaned on further** | `OutdoorAtmosphere.NightAmbientFloor` is unchanged at 0.05 and §6.2's fork keeps all four sides. Noted for whoever holds that fork, as evidence and not as an argument: at phase 0.75 this level's geometry renders **byte-identical regardless of albedo** — every surface in the wide frame resolves to one value — so at present the dark here costs sight completely. That is a measurement of side (a)'s consequence, offered to the side-by-side session the fork is waiting on. The director does not pick. |

**The five, at this altitude.** *Belief:* every surface in this level answers a question when you
look at it — that is the level's stated job. *Break:* none is claimed and none is wanted; this
pass **restores** the belief rather than violating it, because a level whose surfaces are
invisible was failing its own premise, not creating dread by accident. *Why here:* it is not a
beat and does not sit anywhere in a rhythm. *Who perceives it:* everyone, continuously, which is
the mark of infrastructure rather than of a moment. *What changes:* the level's legibility becomes
a **choice** again, which is the precondition for the lake's single illegible surface to mean
anything at all — an unreadable lake in a level of unreadable everything is not a device.

**Dread gate.** *Ambiguity §2.1* — **n/a and deliberately so**; this pass adds no ambiguity and
removes none, and it spends nothing. *The clock §2.2* — **FAIL**, correctly and in the section's
own words: there is no pressure driver in this level and nothing approaches. This is atmosphere
infrastructure, not dread, and adding a clock nobody asked for would be the wrong fix. *Break
§3.1* — **FAIL**; nothing builds. *Incompleteness §3.2* — **n/a**. *Degradation §3.4* — **n/a**;
nothing here is a sanctuary. *Affect §6.0* — **PASS**, and this is the gate the pass exists for.
§6.0's boundary is exact: `LEVEL-BIBLE.md` §8 owns whether a cue can be *perceived*, this file
owns whether perceiving it *feels* like anything. A palette in which the ground and the geometry
standing on it are 1.26 L* apart is a §8 defect first — the level's own state was not perceivable
— and it was starving §6 downstream, because a surface nobody can see cannot make anybody feel
anything. Fixing §8 is the precondition, not the achievement. *Fairness §7.1/§7.2* — **PASS**,
and the same hard requirement as the lake entry applies to the whole level and is now measured:
no rung of this ladder may hide a traversal affordance. Measured in-section, geometry sits 20.6-35.9
L* below its ground and 21.7-23.2 L* below the day sky, so it reads against both backdrops a player
actually has.

**Spike gate — no spike claimed**, and none is available: this is a material set.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; nothing is exposed. *8.2 startle as build*
— **the live risk, and it is answered above**: the TV brightening is a scale on a curve, not a
new peak, so no flash is introduced. *8.3 reliable telegraphs* — n/a; none of these surfaces
precedes anything. *8.4 scripted set-pieces* — not violated; a standing material condition fires
nothing. *8.5 illegible chaos* — **the prohibition this pass is squarely in service of**, from
the unusual direction: the failure being corrected was illegibility with no stake, which is
§8.5's tedium arriving through the art rather than through an event. *8.6 unrelenting tension* —
n/a. *8.7 permanent safety* — n/a. *8.8 streamer-first* — not violated; nothing here is framed
for a camera, and the captures are verification rather than promotion. *8.9 punitive failure* —
n/a, and the lake entry's line stands: **this pass does not authorise a drowning rule.**

**Handed off, unanswered — implementation, not direction:** every value in the ladder (the
builder's, and the director picks none of them); whether the hub's counter is warm and how warm
(`ART-BIBLE.md` and the builder); the emission energy the TV screen actually needs, and the
separate question of **which file gets to own it** — `TvPortal.cs` currently overwrites the
authored screen material at runtime, so the shipped `ShaderMaterial` never renders, which is a
wiring defect for BT-10's owner and not a directorial one; whether `Hub.tscn`'s `no_depth_test`
label is allowed to paint over the counter board (`LEVEL-BIBLE.md` §8 and BT-5/BT-13); and
**which of §6.2's four sides ships**, which is Talon's and is untouched here.

**The level goes darker, and night stops being a wall — directed 2026-08-29 (packet DARK-1b, the
exposure and palette pass and its two addenda, in the instrument level
`scenes/game/world/bubbletest/`).** Altitude: **system** — like BT-9b, this is a rule about how a
whole class of surface is lit, not a beat and not a place. **The Bubble Test still authors no arc,
still has no clock, and this pass does not give it one.** The 2026-08-28 ruling that this level is
not a horror level and must not be directed as one is unchanged and is the frame for everything
below.

**Talon's words are the direction, and they are two constraints rather than one.** *"See both
light and dark it's just that the light mode — that is just the super bright light colors — was
too much. I just like the theme too of dark mode and not so much dark 'mode' but darker in
general."* The first half is a **value** instruction; *see both* is a **legibility floor** under
it. A pass that answers only the first is not a partial success — it is the same defect inverted.
The previous agent's landing point measured 53.2 % of the day frame below luminance 64, and the
red section's cairn resolved to one black silhouette with no readable blocks in it. That is not
darker; it is unlit, and §6.0's boundary is exact about which file owns the difference: a surface
nobody can see cannot make anybody feel anything, so an over-dark frame starves §6 through
`LEVEL-BIBLE.md` §8 in precisely the way BT-9b's over-bright one did, from the other end. **This
pass therefore treats "see both" as the binding constraint and "darker" as the direction under
it**, and lands the day at 1.0 % of the frame below 64 with a mean of 112, from 0.3 % and 241.

**The five, at this altitude.** *Belief:* the level is a well-lit instrument whose surfaces answer
questions — BT-9b's premise, restored. *Break:* **none claimed and none wanted**, the same shape
as BT-9b: this pass restores a belief the shipped state was failing rather than violating one. A
frame with 91 % of its pixels in the top quarter of the histogram was not making anybody feel
calm; it was making the level unreadable while looking safe, which is a legibility failure wearing
an affect costume. *Why here:* not a beat, no position in a rhythm. *Who perceives it:* everyone,
continuously — infrastructure, not a moment. *What changes:* the level acquires a **dark end** for
the first time, and that is the precondition for anything in this file ever being spendable in it.
Value range is the currency every environmental device in §6 is denominated in; a level that
cannot render a shadow tone cannot pay for one.

| Device | Status in this level | Note |
|---|---|---|
| Colour temperature as threat channel | **still `spent-here`; the supporting sentence is AMENDED A SECOND TIME, and this time the margin it rested on is effectively gone** | The 2026-08-27 spend was justified by the lake being the level's one organically-graded and darkest surface; BT-9b recorded the value margin narrowing from 68 L\* to 35 L\* when the geometry rung dropped to 57.9. **This pass drops the neutral rung to L\* 24.4, and `lake_murk.tres`'s silt sits at L\* 22.6 — a margin of 1.8 L\*.** The lake is no longer the dark thing in a level of light things; it is one dark thing among many. **The row's claim survives, but on hue and opacity alone:** the level's neutral carries chroma 0.003 and its grounds 0.124–0.148 in their own section hues, so the murk's olive is still the only surface graded away from the section legend, and BT-9b measured the shipped shader denying depth on its own terms. **What is gone is the value half of the exception, and it is gone because this pass moved everything else — not because anyone touched the lake.** Recorded, not corrected: the 2026-08-28 entry ruled that no number in `lake_murk.tres` was changed by that pass and none should be, and a director does not re-grade a surface to protect its own earlier sentence. **Handed to the lake's owner as a measurement with no instruction attached.** Do not spend this row again inside this level. |
| Night reversal (dark re-signs the map) | **NOT spent, NOT resolved — but this pass MOVES THE EVIDENCE BT-9b OFFERED TO THE §6.2 FORK, and that is the most important line in this entry** | BT-9b recorded, as evidence for Talon's open fork and explicitly not as an argument, that at phase 0.75 this level's geometry rendered *byte-identical regardless of albedo*: the dark here cost sight completely. **That measurement is no longer true of this world.** DARK-1b's addendum B sets a per-world night sight range of 28 m through `OutdoorAtmosphere.SightSource`, so night here costs sight past 28 m instead of past ~7 m. **Three things, stated so nobody mistakes this for a resolution.** (i) `OutdoorAtmosphere.NightAmbientFloor` — the constant §6.2's fork is actually about — is **unchanged at 0.05** and was not touched; the instrument moved is depth fog, a different channel. (ii) The change is **per-world and scoped to a harness**; camp and playtest1 still fall to `PlayerSightCurve.DarkFloorM`. (iii) It is an **assumption flagged as such** — the orchestrator's reading of "see both light and dark" as the MVP plan's option (b), recorded in `docs/agents/handoffs/2026-08-28-TALON-RULING-darker-in-general.md` as its own reading rather than Talon's words. **Consequence for whoever holds the fork: read side (a)'s cost from camp, never from the Bubble Test, because the Bubble Test is now the one world where it is not measured.** The director does not pick and has not picked. |
| Self-lit signage (BT-9b's permission rule) | **rule APPLIED unchanged to a fourth surface — the night-glowing bubbles** | BT-6 specified a night glow on the bubble film; it had never once rendered, because the C# wrote a uniform name the shader does not declare and Godot ignores such a write silently. Fixing it makes ~100 objects self-lit at night, so the licence is checked rather than assumed. **The test, verbatim from BT-9b: does switching it off change what a player can traverse?** No — the bubbles are collectibles standing on platforms that are lit, edged and labelled independently of them, and a player who sees no glow can still cross every platform in the level. **Signage, exempt, no new row, no D10 budget spent.** The edge is worth naming for the next director: the test is written about *traversal*, and the glow does change *findability*. That is where the rule's boundary sits — a surface that tells you where the score is is signage; a surface that tells you where the ground is is an aid. |
| Fog as sightline denial | **borrowed, not spent, and pointed the other way** | §10 has this `seeded` as the *daylight* half of the night dome — fog used to *deny* a sightline. This pass uses the same instrument to *return* one, replacing a ~1.0 m⁻¹ wall with a 0.107 m⁻¹ curve. Using a device backwards is not a spend and must not be logged as one; recorded so a later director reading "fog was used here" does not conclude the denial is burnt in this world. |
| Day/night cycle as clock | **borrowed, not re-spent** | Unchanged from both prior entries. The Bubble Test rides the shipped period and authors nothing. This pass makes the two ends of that cycle *look* different from each other, which is presentation and not a clock. |
| The unreadable dark | **deposit, untouched** | Nothing was shown in or on the water, and nothing in the ground grade reaches it. |

**The first real-time sun shadow in this repo is deliberately NOT registered as a device**, and
the refusal is the same anti-inflation refusal BT-9b made for self-lit signage. A shadow map is an
*instrument*, not a technique that produces dread — it manufactures no feeling by existing. What
is worth recording is that it is a **precondition several `unbuilt` rows have been quietly
assuming**: §6.1's prospect-and-refuge is a claim about what conceals, and until this packet
nothing in this game could occlude anything at all. Whether it ships beyond this world is a
measured performance question, and Talon's.

**Dread gate.** *Ambiguity §2.1* — **n/a**; nothing is spent and nothing is added. *The clock
§2.2* — **FAIL**, correctly, in the same words as both prior entries: no pressure driver, nothing
approaches, and the Bubble Test must not grow one to pass a gate. *Break §3.1* — **FAIL**; nothing
builds. *Incompleteness §3.2* — **n/a**. *Degradation §3.4* — **n/a**. *Affect §6.0* — **PASS, and
it is the gate this pass exists for, approached from the opposite end to BT-9b's.** BT-9b fixed a
palette whose rungs were 1.26 L\* apart; this pass fixes a *frame* whose entire tonal range was one
quarter of the histogram. A level that cannot render a dark surface cannot render a dark mood,
whatever its materials say. *Fairness §7.1/§7.2* — **PASS, and it is the constraint the whole pass
was tuned against.** BT-9b's hard requirement holds and was re-measured: no rung may hide a
traversal affordance. The five ground plates remain iso-luminant within 0.05 L\* of one another and
17.3 L\* above the neutral geometry standing on them — narrower than BT-9b's 20.9, stated plainly
rather than buried, and bought back by a channel the ladder did not previously have: the ground
carries a 1 m checker and the geometry does not, so the pair separates by **pattern as well as by
value**. At night the 28 m range is what makes §7.1 satisfiable at all in the dark half — a player
who cannot see the next block is not being frightened, they are being stopped.

**Spike gate — no spike claimed**, and none is available: this is an exposure curve and a material
set.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; there is nothing to expose. *8.2 startle as
build* — **the live risk in this pass, and it is answered by construction.** Enabling environment
glow is exactly the change that turns an emissive surface into a flash. The bloom is capped in
*contribution* rather than in what a pixel renders as, the wide glow levels (5–7) are off so no
full-screen haze lifts the black point, and the bubbles' night curve is a slow oscillation rather
than a peak — no new peak behaviour is introduced anywhere. **One violation is outstanding and it
is named rather than excused:** `BubbleCounterDisplay.tscn` carries `emission_energy_multiplier =
200`, a value calibrated to punch through the ~1.0 m⁻¹ fog this pass has just removed, and at
close range it now blows a white slab across the night frame. That is a §8.2 failure *caused by
this pass*, and it is **handed to BT-8's owner with the number** rather than edited here — it is a
`.tscn` outside this role's write path. *8.3 reliable telegraphs* — n/a; none of these surfaces
precedes anything. *8.4 scripted set-pieces* — not violated. *8.5 illegible chaos* — **in service
of it, now from both ends**: BT-9b removed illegibility caused by collapsed value separation, this
pass removes illegibility caused by a collapsed histogram, and the ground checker is the third
instrument — a surface a player can measure their own speed against is the opposite of noise with
no stake. *8.6 unrelenting tension* — n/a. *8.7 permanent safety* — n/a. *8.8 streamer-first* —
not violated; every capture in this packet is verification. *8.9 punitive failure* — n/a, and the
standing line holds: **this pass does not authorise a drowning rule.**

**Handed off, unanswered — implementation, not direction:** every number in the exposure ladder
and the palette (the builder's, and the director picks none of them); whether the ground checker
should be quieter or louder than ±9 % of the plate's own albedo, which is a taste call Talon has
not yet seen in motion; `BubbleCounterDisplay.tscn`'s emission multiplier, above, which is BT-8's;
whether `lake_murk.tres` should be re-graded now that its value exception has closed to 1.8 L\*,
which is the lake owner's and is offered as a measurement rather than an instruction; whether the
real-time shadow ships beyond this world, which is a performance measurement and Talon's; and
**which of §6.2's four sides ships**, which is Talon's and is untouched here — with the standing
warning above that this world's night is no longer usable as evidence for it.

---

**The tangle — directed 2026-08-29 (packet TANGLE-1, the seventh section of the Bubble Test,
`scenes/game/world/bubbletest/sections/Tangle.tscn`).** Altitude: **scene** — one 23 m pile in
one section. The level altitude is not claimed, for the reason BT-2 already recorded: the Bubble
Test has no session arc, and directing one onto a movement rig invents a feeling the artefact
cannot hold. BT-2's finding stands verbatim and is not re-litigated here: **this level is not a
horror level and must not be directed as one.**

**What is new is that this is the first section of it with a reason to go somewhere.** The other
six state their route — a beam, a ladder, a line of stones, a flat run, a plaza with signposts,
a sealed room — and each one's route *is* its content. The tangle states nothing. It is 107
blocks a generator threw at a seed, with a 23-tread spiral stair hidden inside them and one
bubble floating over the top of it, visible from the hub 110 m away and from every metre of the
approach. The affect available without a clock, a threat or an entity is §1 creepiness in its
narrow sense — ambiguity about how to read a thing — and this section supplies it the way the
lake does, from the other side: **the lake shows you the route and hides what stepping wrong
costs; the pile shows you the prize and hides the route.**

The direction in one line: **you can see exactly where you are going and nothing in the world
will tell you how to get there.**

**THE PACKET'S CLAIMED DEVICE IS REFUSED AS STATED, AND THE REFUSAL IS THE USEFUL PART.** The
packet names "the looming approach — the canted slabs are the device". The slabs are excellent
composition and they are not a device: looming is a *silhouette*, and §2.1 requires something
that does not resolve. A 9 m slab canted over the path resolves completely the instant you look
up at it — it is a static block, it is not going to fall, nothing is under it, and by the second
approach it is a landmark. Registering it would have put a decoration in a register whose own
warning is that an inflated §10 is worse than an absent one. What the slabs actually earn is
**§4.5 agency: they are the reason the pile reads as a place rather than as an obstacle**, and
that is a composition note for the Level check, not a ledger entry. It is recorded as a refusal
so the next director does not re-propose it as fresh.

**What IS registered is a new §10 row — *The unmarked mass*** — with its six-way anti-inflation
check written into the row itself, including the *Scale that is not the player's* near-miss,
which is the one worth reading: the tangle has the same silhouette that row wants and inverts its
mechanism, because its human band is the *fullest* part of the pile rather than the emptiest.
**Its decay state follows BT-10's ruling exactly: `spent-here` for `BubbleTest` and available
game-wide.** A test-harness spend is not a game spend, and the device's real first use — a
climbable mass in the camp woods, at night, where being wrong about a route costs something —
is unspent and still owed its own approach.

| Device | Status in this level | Note |
|---|---|---|
| **The unmarked mass** | **NEW ROW — directed and built; `spent-here` for the Bubble Test only** | See §10. The three constitutive constraints are met by the shipped geometry and each is checkable: (a) the summit bubble is visible from the hub and from the whole approach, the stair is not; (b) exactly one route is guaranteed and it is unsigned — `tools/dev/tangle_stair_check.gd` asserts every tread walking, single held jump, no run-up, no air jump, and the only marked surface in 107 blocks is the last tread; (c) failure is the walk back down, and the level has no other cost to spend. |
| **The looming approach (canted mass overhead)** | **REFUSED — not registered, not spent** | See above. Composition, not a device; it resolves on first look and §2.1 has nothing to work with. |
| Scale that is not the player's | **near-miss, recorded, NOT spent** | Written into the new §10 row rather than here, because the inversion is what makes the new row necessary. The pile is 23 m and the human band is its densest layer: it is a mass sized *for* a climber, which is the opposite claim to "not built for you". BT-2 recorded the same row as not-spent for a duller reason (walkable column tops); this one is a real inversion and worth the words. |
| Colour temperature as threat channel | **untouched — tenth occurrence of the same trap, and the closest call so far** | The tangle takes hue 300 (violet) because it is the only gap left on the wheel, and violet is a cool hue. **It is not a grade and must never become one.** The row is already `spent-here` in this level on `lake_murk.tres`, and that entry's own words are that a second graded surface makes the first one decor. What ships is a section *tint* solved on BT-9b's ladder to the same L\* as the other five grounds (41.67 against 41.65–41.73) — iso-luminant, one rung, no shift in any direction. The distinction is exactly the one BT-2 handed to BT-9: **this earns its place by hue-position, the lake earns its by value.** If a later pass darkens or cools the tangle *relative to the level*, it has spent this row and must say so. |
| The unreadable dark | **not spent, and not available** | The murk withholds by opacity with nothing behind it. The pile withholds by clutter with everything behind it — you can see every block; you cannot see which sequence of them is a route. Different instrument, and the new §10 row exists precisely so this one is not stretched to cover it. |
| The apparent artefact | **QUARANTINED, unchanged** | BT-2's quarantine transfers verbatim and this section strengthens it rather than testing it: the tangle is irregularity with no maker, which is the artefact row's own inverse. Nothing here is the camp pocket's first appearance. |
| Wrong silence | **REFUSED — tenth occurrence** | Do not wire `AmbientBed.Withdraw()` to the section volume, to entering the pile, or to the summit. A section boundary is a footstep-material change (the postpile entry's words) and standing under a slab is the most explicable acoustic change there is. |
| Day/night cycle as clock | **borrowed, not re-spent** | The section rides `RunDriver`'s shipped period and authors nothing. It has no clock of its own and must not grow one. |
| Night reversal | **leaned on, not resolved** | `MinAmbientEnergy` untouched; the §6.2 fork is not moved. The summit tread's `night_edge_tangle` rim is D10's shipped legibility rule applied to a seventh section, authored by the program — recorded so nobody counts it as this row's substance. DARK-1b's standing warning still holds: this world's night is not usable as evidence for the fork. |
| Landform as disorientation | **not spent — inverted, as BT-2 inverted it, for a different reason** | You can see the pile whole from outside and you can see where the top is from anywhere on it. What is unreadable is the *route*, never your position, and the difference is §7.2: a space that lies about where you are is broken, a space that declines to name its route is a puzzle. |
| Fog as sightline denial · Cost-to-know instrument · Accumulating wrongnesses · Persistent scarring · A world that breathes · Doorway as proscenium · Stairs that lie · Non-predictive threshold · Route loss · Sound with no visible source · Synced audio-visual peak · Flash beacon · Forced separation · Teammate voice cut-out · Contagious panic · The blind hide · Attention as a resource · Authority withdrawn · Position without judgment · The watched · Refuge maintained only by leaving it · Open sanctuary inverted by dark · The ration that never refills · Rising tide · Proximity voice falloff · Tension-budget pullback · Canopy as spatial darkness · Per-player sensory asymmetry · The bounded exception · The object that is a door · Glow-stick trail | **untouched** | None is placed, leaned on or implied. The section places no creature, authors no clock, withdraws no sound, grants no capability and takes nothing from anybody. |

**Dread gate — six of seven fail, and every failure is correct.** *Ambiguity* (§2.1) — **pass**,
narrowly and honestly: the route does not resolve until you have climbed it, and it is the one
thing in this level that a player cannot read from the pavement. *The clock* (§2.2) — **fail**,
by design; there is no clock, the Bubble Test must never author one, and this section is
therefore atmosphere rather than dread, in those words. *Break* (§3.1) — **fail**; nothing
builds, so nothing breaks. *Incompleteness* (§3.2) — **fail**; reaching the summit is a complete
all-clear. *Degradation* (§3.4) — **fail**; the pile is permanently safe and the route, once
learned, stays learned. *Affect* (§6.0) — **pass**, and it is the item that carries the section:
the pile is perceivable from 110 m and perceiving it produces a specific want, which is the whole
budget a movement test can honestly spend. *Fairness* (§7.1, §7.2) — **pass, and measured**: the
stair's worst rise is 1.202 m against a 1.407 m held-jump apex, 85 %, with every tread asserted
against the arc the motor actually has rather than the one the lab was built on. "Avoidable but
barely" here means *reachable but not obvious*, and the rules do not change on you.

**Spike gate — no spike claimed.** Conditions 1 and 3 are unavailable in this level (nothing is
at stake and nothing is simultaneous), and §4.4's rarity rule would refuse one here anyway.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a, no threat. *8.2 startle as build* —
**not violated, and one thing was watched for it**: the summit bubble's night glow is a
continuous emitter that is visible before it is reached, never a thing that appears; nothing in
the section animates in response to arrival. *8.3 reliable telegraphs* — n/a; nothing here
precedes anything. *8.4 scripted set-pieces* — **not violated, and this is the section's
strongest suit**: the pile is one seed's jitter, the rubble produces overhangs and tunnels nobody
authored, and the only authored thing in it is the stair's reachability. *8.5 illegible chaos* —
**the closest call in the pass, and it is answered by construction**: §8.5 forbids noise with no
discernible stake or cause, and the stake here is stated by a visible reward with a guaranteed
route to it. A pile with no summit bubble would be the violation; the bubble is what makes the
clutter a question rather than a mess. *8.6 unrelenting tension* — n/a. *8.7 permanent safety*
— **failed, and correctly**: see the dread gate. *8.8 streamer-first* — not violated; every
capture in this packet is verification. *8.9 punitive failure* — **not violated and pinned as
constraint (c) of the new row**: falling off the pile costs the walk back up, the section sits on
its own ground plate above the kill plane, and nothing in it may ever cost more than time.

**Handed off, unanswered — implementation, not direction:** whether the pile's three rock tones
want more or less spread than ±5 L\* (art-atmosphere's, and a taste call Talon has not seen in
motion); whether the tangle should eventually get an ambient bed distinct from the rest of the
level, which is the sound family's and is not asked for here; whether the stair's 85 % apex
margin *feels* right at the controls, which no capture can answer and only Talon can; and
whether a climbable mass belongs in the camp woods at all, which is a level question for
whenever the camp gets its next pass — flagged, not proposed.
**The lake that now decides — directed 2026-08-29 (WATER-3: a 3.0 s submersion death and a
bounded lake, in the instrument level `scenes/game/world/bubbletest/`).** Altitude: **system** —
a death and the hazard that causes it, not a beat and not a place. The `scene` altitude is
**already directed** by the 2026-08-28 green-crossing entry above, and this pass does not
re-direct it; it **amends** it, because WATER-3 makes one sentence that entry wrote as a
guarantee false. The `level` altitude stays unclaimed for the reason both prior Bubble Test
entries give: directing an arc onto a harness would make it a worse harness. *(Ledger read on the
reconciled register — BT-2's, BT-10's, BT-9b's and DARK-1b's entries are all present on this
branch, so no §12 fork warning applies to this read.)*

**Talon's word, and it is a ruling rather than a proposal:** *"there is a problem with the water
as it currently is the water should kill the player after about three seconds, and this does not
happen."* The director does not re-open that. What the director owes is the affect consequence,
and there is exactly one that matters.

**The one finding: the 2026-08-28 entry's §8.9 guard was written as a fact, and this pass makes it
false.** That entry states, verbatim, *"every failure is a swim and a walk"* and *"the cost of
falling is time, never a run."* The second half still holds — no run exists to lose, the bubble
counter is world state and survives a death, and this level carries nothing, so `Props` is null
and a drowning drops nothing. **The first half is now wrong.** A missed stone was a swim to the
bank; it is now a death, a comic beat, and a walk back from the hub roughly 95 m away. The cost of
falling went from about ten seconds to about forty. That is still time and still not a run, so
**§8.9 passes — but it is now this section's tightest prohibition rather than an untouched one**,
and it is recorded here so the next director does not read the older entry as current.

**What the drowning is FOR, in affect terms, and it is not the death.** The green crossing's whole
declared affect budget is one sentence: *"you can read the whole crossing before you step, and you
cannot read the one thing that would tell you what stepping wrong costs."* Until WATER-3 that
sentence was **structurally untrue** — stepping wrong cost nothing, so the murk was ambiguity with
no stake behind it, and §2.1's fuel was being spent on a question whose answer did not matter. The
drowning does not resolve the ambiguity; it puts something behind it. **The hesitation that entry
asked for is only now available**, and that — not the death — is what this packet directs.

**And the bounding is a §7.2 fix, which is why it is one packet with the death and not two.** Water
that kills where no water is rendered is not ambiguity about *presence*, it is arbitrariness about
*rules*, and §7.2 draws that line explicitly: *"a threat whose behaviour changes without cause is
not mysterious, it is broken, and players correctly read it as unfair."* The half-plane made the
whole western half of the world silently lethal the moment a timer existed. Bounding the lake to
what GreenHills renders is the cheapest possible §7.2 compliance: the hazard is exactly the thing
you can see.

| Device | Status in this level | Note |
|---|---|---|
| **Cost-to-know instrument** | **considered and REFUSED as a spend — the row stays `unbuilt`** | The temptation is obvious and wrong. BT-2 recorded the murk as the honest statement of this section's ceiling: *"the murk withholds information and offers no trade to get it."* WATER-3 creates something that looks like the trade — you can learn how deep it is by getting in — but §2.3's device requires the information to be *usable afterwards*, and here buying it can end the purchase. A one-way trade is a hazard, not an instrument. Logging this as the repo's first cost-to-know would burn an `unbuilt` row on a degenerate case and inoculate the real one — the same mistake the neighbourhood-ring inoculation finding names. |
| **The unreadable dark** (murk half) | **deposit posture unchanged — seventh surface, still not spent** | The murk is still opacity with nothing behind it. Nothing is ever *in* the water; the drowning is a property of depth, not of an occupant, and it must stay that way. **Binding: do not put anything in this lake to make the death land.** That would spend the deposit on a harness, and it is the one way this pass could damage a device it is otherwise leaving alone. |
| Wrong silence | **REFUSED — tenth occurrence** | Do not wire `AmbientBed.Withdraw()` to submersion, to the drowning clock, or to the death. BT-2 already refused it for submersion and the reason is unchanged and now doubled: going under water is the most explicable absence there is, and a death is the most explicable of all. |
| Colour temperature as threat channel | **untouched** | The drowning authors no colour. The standing requirement from BT-2 and BT-9b — the murk reads darker and lower-chroma, never bluer — is not moved. |
| Day/night cycle as clock | **borrowed, not re-spent** | Unchanged. **And the drowning clock is explicitly NOT a §2.2 clock and must never be built as one:** it is per-player, invisible to everyone else, self-initiated, and reset by surfacing. It has none of the four properties (visible, shared, monotonic, inevitable). The Bubble Test still has no pressure driver and must not grow one. |
| **Per-player sensory asymmetry** | **produced, not spent — the posture BT-2 recorded** | A drowning peer's last three seconds are invisible to everyone but them (the swimmer's eye sits at the waterline); the bank sees a head, then a comic loft. Free, unauthored, and not claimed as a spend. |
| Night reversal · Fog as sightline denial · Landform as disorientation · Scale that is not the player's · The apparent artefact · The bounded exception · Accumulating wrongnesses · Persistent scarring · A world that breathes · Doorway as proscenium · Stairs that lie · Non-predictive threshold · Route loss · Sound with no visible source · Synced audio-visual peak · Teammate voice cut-out · Proximity voice falloff · Forced separation · The object that is a door · Flash beacon · The watched · The blind hide · The ration that never refills · Authority withdrawn · Attention as a resource · Position without judgment · Contagious panic · Infrastructure as the attack surface · Tension-budget pullback · Canopy as spatial darkness · Rising tide as clock · Open sanctuary inverted by dark | **untouched** | None is used, leaned on, or moved. Named so a later reader can tell "not used" from "not checked". |

**The five questions.** *Belief* — "the water is scenery; the stones are the test." *Break* — the
instant the head goes under and does not come back up. *Why here* — it is not a placed beat and
does not claim to be; the honest answer is **"because the composition already put the water under
the only route, and until now it meant nothing."** *Who perceives* — everyone on the bank, in the
same second, through a comic loft that reads at distance (§4.1 condition 3 is met incidentally,
not designed for). *What is true afterward* — the group knows the water is not scenery, and the
third stone reads differently on the way back.

**Dread gate.** *Ambiguity* (§2.1) — **pass, and strengthened**: the murk's ambiguity now has a
stake behind it and nothing resolves it; a drowned player learns the water is deep *there*, which
is one sample, not the map. *The clock* (§2.2) — **fail, correctly, and the fail is load-bearing
here**: this is atmosphere and a hazard, not dread, in those words. The drowning timer is not a
clock (see the table). *Break* (§3.1) — **fail**; there is still no build in this level to break.
*Incompleteness* (§3.2) — **fail**; reaching the island is still a total all-clear. *Degradation*
(§3.4) — **fail**; the island is still permanently safe (§8.7, the knowing violation BT-2
recorded). *Affect* (§6.0) — **pass, and this pass is what earns BT-2's conditional**: that
entry's affect pass was conditional on the murk being opaque, and even opaque it was withholding
the answer to a question with no consequence. Now the question has one. *Fairness* (§7.1, §7.2) —
**pass on §7.2, CONDITIONAL on §7.1 with a number the director does not own.** §7.2 is satisfied
by the bounding, which is most of why the bounding is in this packet. §7.1 wants *avoidable but
barely*, and the derived arithmetic lands almost exactly there — which is the good outcome and
also the fragile one. See handoff 1: the margin is under a second, and it is derived, not
measured.

**Spike gate — no spike claimed, and none is available.** Condition 1 (stake) fails at the level:
nothing in the Bubble Test is on the line, and a death costing a walk is not a stake. Condition 3
(simultaneity) is incidentally satisfied and condition 4 (legibility — a stranger can see someone
fall in and drown) outright, but two of four is not a spike, and manufacturing the missing two in
a harness would inflate the currency for the levels that need it (§4.4).

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; nothing is exposed, and the binding above
keeps it that way. *8.2 startle as build* — **not violated, and worth saying why it could have
been**: the drowning is the *release* of a micro-arc the player entered by choice, firing three
seconds after their own commitment with the outcome visible throughout. A drowning without the
three seconds would be startle. *8.3 reliable telegraphs* — none authored; the murk is not a
warning and must not become one. *8.4 scripted set-pieces* — not violated; who falls in is
emergent and is the whole comedy. *8.5 illegible chaos* — actively served: the hazard's extent is
now exactly its render. *8.6 unrelenting tension* — n/a. *8.7 permanent safety* — BT-2's knowing
violation stands and is **slightly reduced**: the island is still permanently safe, the route to
it no longer is. Recorded, not claimed as a fix. *8.8 streamer-first* — not violated; nothing here
is staged. *8.9 punitive failure* — **the tightest item in this pass, passing.** The cost rose
from about ten seconds to about forty; it is still time and never a run, and the two things
keeping it passing are that the death drops nothing and that the bubble counter survives it.
**If either changes, this line is where it breaks.**

**Canon updated:** this entry (new, §12); one fork added to §13. **No device row in §10 was
changed, and that is deliberate** — the cost-to-know refusal above is the whole reason, and
recording a refusal as a spend is how a register starts lying. §6.2 is not moved and no fork is
resolved.

**Handed off, unanswered.**

1. **The one that needs Talon's eye, and the director explicitly does not resolve it: the swim-out
   margin.** Derived from the shipped tuning — swim speed is `MoveSpeed × SwimSpeedMul` =
   3.8 × 0.40 = **1.52 m/s**, of which about 0.3 s goes on settling to the swim line, so a 3.0 s
   window buys roughly **4.1 m of swimming**. From the bake's bowl profile the water is over your
   head out to about **r = 13.9 m**, and the innermost stepping stone sits at about **r = 9.9 m**
   — so a player who falls off the last stone and swims *straight out* needs about **4.05 m**, and
   one who swims toward the island meets a wall built to be unclimbable from the water. That is
   "avoidable but barely" to within a hair, which is §7.1's target and is also no margin at all
   for reaction time. **Three sides, not two** — the window (3.0 s, Talon's), the swim speed
   (`SwimSpeedMul`, the water contract's), and the stone spacing (`GreenHills.tscn`, already
   flagged by MOVE-8 as breaching `StoneGapMax` against the post-ruling tap range). The numbers
   above are **derived, not measured in engine**, and measuring them is the first thing the next
   pass should do.
2. Whether a drowning should read differently from a fall. `RespawnCause` carries the cause on the
   `Died` event and nothing consumes it; both causes play the same loft-and-spin. That is
   `MECHANICS-BIBLE.md` §2.5 territory and a presentation packet's, not the director's.
3. Whether the six island bubbles being reachable only across a crossing that now punishes failure
   is acceptable for a *collection* harness — BT-11's and the layout's, not the director's.
4. `Bubble_Green_16` sits at local y = −0.65 against a surface at −0.58, i.e. **7 cm under the
   water**, against BT-2's explicit ruling that none may sit under the surface. It is wading depth
   there, so no drowning risk; reported as a pre-existing BT-11 breach and not touched.
5. Every value: the window, the swim speed, the stone spacing, and whether going under wants any
   audible cue at all (`sound-*` — the standing refusal of wrong silence above applies to any of
   them).

**The reset lever's warning, and what an honest cue is not allowed to borrow — directed
2026-08-29 (packet LEVER-1, the Bubble Test hub).** Altitude: **system** — this is a rule about
a class of cue, not a beat and not a place. The request arrived already argued, with a position
("flat and administrative") and an instruction not to rubber-stamp it. The position is upheld,
its **first** reason is upheld and sharpened, its **second** reason is refused as belonging to
another file, and two constraints are added that the request did not contain.

**What was built.** A lever that wipes the shared bubble tally now asks first: one pull turns its
four diegetic sign plates from what it does ("RESET / ALL BUBBLES RETURN / COUNT BACK TO ZERO")
into a warning carrying the live number at risk, reddens the handle, plays one flat positional
click, and broadcasts that state to every peer for a few seconds. A second pull inside the window
wipes; walking away or waiting withdraws it. Talon's own words are the acceptance: *"I would be
upset if I spent hours collecting all the bubbles and then someone reset my progress."*

**The five.** Belief: *pulling this lever is a thing I can simply do* — which was **true**, and
cost the group its board. Break: **none, and that is the finding.** This cue does not violate a
belief; it corrects one. Why here: affectively, nowhere — it is a rail, and it is on the one
control in the level that can destroy a session's work. Who perceives it: **everyone**, and the
broadcast is deliberate, but for coordination (a teammate at the board can say something) and not
for contagion. What changes afterward: the group can see that somebody is about to wipe the board,
which is the whole of it.

**The dread gate does not run here, by standing ruling and not by exemption.** The 2026-08-28
entry above already ruled that the Bubble Test is not a horror level and must not be directed as
one, with six of seven dread-gate items failing correctly. Nothing in this pass reopens that.
Ambiguity: refused on purpose — this cue exists to *remove* uncertainty and it may not be graded
on spending fuel it is chartered to return. Clock: **the one item worth stating as a refusal
rather than as an N/A — see below.** Break, incompleteness, degradation, fairness: not claimed,
not applicable. Affect (§6.0): **deliberately none**, which is this entry's whole content.

**No spike claimed — and the reason to say so at length is that this beat is accidentally
spike-shaped.** Run §4.1 honestly against a wipe and it very nearly passes: there is a stake
(hours of collecting), a violation (the board gone), simultaneity (six peers watch one tally hit
zero) and legibility (a stranger understands the clip instantly). That is precisely why it must
not be dressed. §4.5 fails and fails hard — **the person who loses the board did nothing**; the
violation is downstream of somebody else's misclick, not of a decision the group made. Giving
this beat affect would manufacture a spike out of a UI error, which is §8.5 (a stake with no
cause the group can own) wearing §4's clothes.

**The requester's first reason is upheld, and the correct version of it is stronger than the one
offered.** The argument given was: affect here teaches the player that the game stings before it
takes something, and the night creatures must not inherit that telegraph. That is §8.3 and it is
right, but the reason it is right is not "this is a sandbox." **§8.3's own parenthetical splits
cues into two classes that must never share a channel: a warning that a state is changing is a
*fairness contract* and stays honest; a hint that something is about to happen is a *dread
device* and must sometimes lie.** This lever's warning is unambiguously the first class. Honest
cues do not get dread grammar — not because the level is a harness, but because the class
forbids it. That reason survives this level becoming a real level; "it's only a test world" does
not, and would have quietly licensed dressing the same cue the day it shipped in camp.

**The requester's second reason is refused as a reason.** "It would add a second blooming emitter
to a hub Talon just called overblown" is a true and useful constraint, and it did change the
build — the armed handle came down from an energy that measured as a blown streak to the energy
the highlight already used, carrying the state in colour instead. But that is an `ART-BIBLE.md`
and render-budget argument, not a Thrill one, and it evaporates the moment someone fixes the glow
threshold. A direction that rests on it would be a direction with an expiry date. Recorded as
corroborating; not load-bearing.

**Amendment: flat does not mean invisible.** `MECHANICS-BIBLE.md` §2.5 asks that two states a
player can sit in for more than a moment be distinguishable from *each other*, within about a
second, without reading text. Four seconds of red plate and red handle is exactly that and must
stay. **The line, and it is checkable: flat means no MOTION, not no signal.** A single state
change is administrative; a state change with a *tempo* is dread grammar. So: no pulse, no throb,
no ramp, no accelerating tick, no stinger, and nothing about the armed state may change while it
is armed. If any property of this cue animates over the window, it has crossed the line, and that
test needs no judgement to apply.

**Refusal: this cue may never be given a countdown readout.** No shrinking bar, no ticking number,
no closing ring, nothing that renders how much of the window is left. That is §2.2 — the visible,
shared, monotonic, inevitable approach — which is this file's single strongest and cheapest dread
instrument, and spending it on a confirm dialog in a test harness would be the most expensive
burn available for the least return in the register. The window must be experienced as *it went
away*, never as *it is running out*. Currently satisfied: nothing renders the remaining time, and
nothing may be added that does.

**Devices: none spent, and two near-misses logged so a later director does not read them as
burns.**

- ***Colour temperature as threat channel* (§6.6) — NOT spent.** The plate and handle go warm-
  to-red, which is literally a directed colour shift. §6.6 is about a *place* changing sign — a
  warm room gone cold, a statement the world makes about itself. This is one prop's instrument
  panel reporting its own state at a scale of half a metre. Instrumentation, not weather. The
  device stays as this ledger already has it, for the Bubble Test and everywhere else.
- ***Synced audio-visual peak* (§4.6) — NOT spent.** The click and the colour land in the same
  instant, which is the technique. But §4.6's device exists to make a *spike* land; here the
  alignment is redundancy under `INTERACTION-BIBLE.md` §8.2, so a player whose audio is severed
  still gets the warning. Same mechanism, no affect, no spend.

**The sign itself passes BT-9b's signage licence verbatim,** which is why it needs no separate
ruling: switch every plate off and a player can still traverse every platform in the level. It
reports the instrument's own state and reveals nothing about the world, so it is signage and
exempt from D10's budget — the same test the bubble counter passed.

**Prohibitions.** 8.1 over-exposure — nothing is exposed; there is no threat here. 8.2 startle as
build — refused explicitly; the click is an acknowledgement at conversational volume and the
armed state is a held state, not a hit. 8.3 reliable telegraphs — **the load-bearing one**, and
the cue is deliberately in the honest class where reliability is a contract rather than a defect.
8.4 scripted set-pieces — this fires identically every time, which for a fairness cue is required
rather than forbidden; §8.4 governs dread beats. 8.5 illegible chaos — the opposite: the number
at risk is on the plate. 8.6 unrelenting tension — none authored. 8.7 permanent safety — not a
sanctuary. 8.8 streamer-first — the cue is designed for the person about to make a mistake, not
for a camera; the fact that a wipe would clip well is an argument *against* dressing it, above.
8.9 punitive failure — the packet's whole content: the wipe is the punishment, and this makes it
require a decision rather than a twitch.

**Handed off, unanswered.**

1. A peer joining *during* an armed window is not told about it, so a late joiner walks into a hub
   whose sign says one thing on five machines and another on theirs. That is `MECHANICS-BIBLE.md`
   state-sync territory and programming's, not the director's — flagged because the window is
   short enough that it may reasonably be left alone.
2. What the plate should say when the tally at risk is **zero**. It currently reads honestly
   ("WIPE 0 BUBBLES"), which is correct and slightly comic; whether a control with nothing to
   destroy should warn at all is an interaction question.
3. Every value: the window, the minimum dwell, how red, how loud. Playtest calls, and the director
   does not pick them.

---

**The signage licence has a first half, and nobody was checking it — directed 2026-08-29
(packet COUNTER-1, the bubble counter that had become a lamp).** Altitude: **system** — this is
a condition on a class of surface, the same altitude and the same class BT-9b opened on
2026-08-28. It is not a beat, it is not a place, and it opens no new §10 row for the same reason
BT-9b refused one: a permission boundary is not a technique.

**What happened, because the mechanism is the finding.** BT-9b licensed self-lit signage with a
one-line test — *a self-lit surface may report the instrument's own state but never reveal the
world; does switching it off change what a player can traverse?* Every director since has checked
the **second** clause and none has checked the first. Meanwhile the board's emission was raised
to 200× against a night fog, that fog was removed by DARK-1b, a glow pass was added in the same
packet, and the board became a blown white slab: measured headed at 1920×1080, 31.4 % of it
clipped at night from 6 m, 51.4 % clipped in daylight from 20 m, and the count unreadable at both
ranges in both states. Talon played it and said it was *"completely over blown … too bright …
and it's blurry."*

> **The condition, stated so it can be checked (extends BT-9b's rule; does not replace it).**
> **A self-lit surface is licensed BECAUSE it reports the instrument's own state — so a self-lit
> surface that has stopped being readable has lost the thing that licensed it.** The exemption is
> conditional on the report, continuously, not granted once at authoring time. The check is not
> *"is it bright enough"* — brightness is what failed here — it is *"can the report be read at the
> ranges the level asks for it"*, and it is answered by a capture, never by a value.

**And the second clause is verified rather than assumed, because I expected it to have failed and
it had not.** The obvious reading is that a 200× emitter had become the hub's light source, i.e.
had started revealing the world. **Measured, it had not**, and the honest number belongs in the
ledger: across three night cameras the frame's ground band moves 34.79 → 35.23, 10.78 → 10.81 and
10.20 → 10.17 between the 200 and the re-derived value — nothing, because emission plus a
screen-space bloom lights no geometry. The whole-frame drop (23.55 → 16.67 on the near camera) is
the board's own blown area and its halo and nothing else. **So the violation was of the FIRST
clause only, and that is precisely why nobody caught it: the clause everyone was checking was
still passing.**

**Why this is a doctrine finding and not a tuning note.** The emissive value was honestly measured
when it was set (FIX-1, against a ~1.0 m⁻¹ fog that crushed the glyphs to a peak of 0.395 at 6 m).
Its *premise* was then deleted underneath it by a different packet, silently, and no gate in the
repo could notice — the unit test asserted a one-sided floor, so the only direction it could catch
was the one that had already been fixed. **An affect value is a reading of the atmosphere it sits
in, and this file should expect atmosphere changes to invalidate readings taken against the old
one.** The general rule for the next director: when a pass changes fog, exposure, sight range,
tonemap or glow, the self-lit surfaces calibrated against the old atmosphere are **findings of
that pass**, not inheritances for whoever owns them next. DARK-1b did exactly the right thing —
it named this board in its own §8.2 sweep and routed it with a number rather than editing another
role's file — and the routing still cost a playtest, because a routed finding sits unread until
someone plays the build.

| Device | Status in this level | Note |
|---|---|---|
| Self-lit signage (BT-9b's permission rule) | **rule EXTENDED with its first-half condition; register still NOT inflated** | The clause above. Applied to the counter board and re-verified for the section labels and the bubbles by the same test — all three report a state and none of them reveals ground, and all three are readable at the ranges the level asks for. Still no §10 row: this is a boundary on D10, not a technique, and §10 starts lying the moment it holds things that are not devices. |
| The object that is a door (BT-10, the TV) | **untouched — but its constraint is now the general case** | BT-10's row carries "may be the brightest thing in the room and must not become the room's light source" as a bespoke constraint on one screen. This pass finds the same shape on an unrelated surface, so the constraint is recorded above as a property of self-lit surfaces generally. The TV's own row and its four constraints are unchanged and unspent. |
| Night reversal | **leaned on, not resolved; and the §6.2 fork's evidence is UNAFFECTED by this pass** | `OutdoorAtmosphere.NightAmbientFloor` untouched at 0.05, and this pass does not go near it. Worth one line for whoever holds the fork: the board never lit the ground, so removing 99 % of its emission changes nothing about what the dark costs here. DARK-1b's standing warning still holds — this world's night is not usable as evidence for the fork. |
| Colour temperature as threat channel | **still `spent-here`, not re-spent** | The board's emission hue (a cool blue-white) is unchanged; only its energy moved. No new statement is made in that channel. |
| Day/night cycle as clock | **borrowed, not re-spent** | Unchanged from all four prior entries. The Bubble Test rides the shipped period and authors nothing. |

**The five, at this altitude.** *Belief:* every surface in this level answers a question when you
look at it — BT-9b's premise, restored again. *Break:* none claimed and none wanted; like BT-9b
this pass **restores** the belief rather than violating it. A scoreboard nobody can read was
failing the level's own premise, not creating anything by accident. *Why here:* it is not a beat
and sits nowhere in a rhythm; it is infrastructure, and the honest answer to "why here" is
"because Talon could not read the number". *Who perceives it:* everyone, continuously — the mark
of infrastructure rather than of a moment. *What changes:* the hub's one lit object goes back to
being a **sign** rather than a **lamp**, which is the distinction the licence was written to
protect and had stopped enforcing.

**Dread gate.** *Ambiguity §2.1* — **n/a and deliberately**; nothing is spent and nothing is
added. *The clock §2.2* — **FAIL**, correctly and in the standing words: the Bubble Test has no
pressure driver, nothing approaches, and it must never grow one to pass a gate. *Break §3.1* —
**FAIL**; nothing builds. *Incompleteness §3.2* — **n/a**. *Degradation §3.4* — **n/a**; nothing
here is a sanctuary. *Affect §6.0* — **PASS, and it is the gate this pass exists for.** §6.0's
boundary is exact and this case sits right on it: a scoreboard blown to a featureless slab is a
`LEVEL-BIBLE.md` §8 legibility defect *first*, and it was starving §6 downstream — a surface whose
report cannot be read cannot make anyone feel the satisfaction of a rising count, which is the one
affect this level's instrument has. Fixing §8 is the precondition, not the achievement.
*Fairness §7.1/§7.2* — **PASS**; the rule the board states is the same rule it stated before, and
it now states it legibly at both of the ranges the programme asks for.

**Spike gate — no spike claimed**, and none is available: this is one material value on one prop.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; there is no threat to expose. *8.2 startle
as build* — **the outstanding violation DARK-1b named is CLOSED, and this line is the closure.**
DARK-1b's own sweep recorded `BubbleCounterDisplay.tscn`'s 200× multiplier as a §8.2 failure it
had caused and handed on with a number. It is re-derived from a capture ladder rather than from
that hand-off's proposed figure, and — recorded because it matters to how routings are read — the
proposed figure was **measured and found insufficient**: at that value the halo is still there and
the board is still gone behind it. Nothing in this prop's behaviour changed; no curve was
reshaped, so no addition and no strobe. *8.3 reliable telegraphs* — n/a; the board precedes
nothing. *8.4 scripted set-pieces* — not violated. *8.5 illegible chaos* — **in service of it**:
this is the third pass in a row removing illegibility, and this one removes the kind caused by too
much light rather than too little, which is a direction the previous two never had to look in.
*8.6 unrelenting tension* — n/a. *8.7 permanent safety* — n/a. *8.8 streamer-first* — not
violated; every capture in this packet is verification, and the fix is downstream of a player
complaint rather than of a camera. *8.9 punitive failure* — n/a.

**Handed off, unanswered — implementation, not direction:** the emissive value itself and every
number on the ladder (art-atmosphere's, and the director picks none of them); whether the board's
own `OmniLight3D` should stay at its shipped energy now that the board no longer dominates it —
measured as contributing the entire pool of light on the plinth and nothing to the blow-out, and
offered as a measurement rather than an instruction; whether the counter's face should carry a
darker housing now that the glyphs no longer wash it, which is a taste call Talon has not seen;
and **which of §6.2's sides ships**, which is Talon's and is untouched here.

**The flashlight — directed 2026-08-29 (NIGHT-2, Talon's notes 10 and 11: "when night falls, it's
extremely difficult to see anything" / "give the player a simple 'glow' when they 'toggle' this
flashlight").**

**Altitude: `system`.** Not `scene` and not `absence`. It is a toggle with an on state and an off
state and no position in any rhythm — the question this file gets to answer about it is *what
feeling does this system manufacture at all*, which is the `system` row verbatim.

**Feeling.** *You are carrying the edge of what you can see, and it is small.* The design pressure
was to answer "I can't see" with more light, and the honest reading of his two notes together is
that he was not asking for the night to be brighter — he was asking for a **tool**, and he wrote
"this flashlight" as though one already existed, which is a player telling you the affordance is
missing rather than the level. So the direction is: give him agency over a **radius**, not over the
night. What that produces is not relief; it is a moving boundary he now owns and can watch fail to
reach things. A five-metre pool in a hundred-metre arm is a statement about how much of the world
he does not have.

**The two constraints that are constitutive rather than tuning, and both were honoured.**
**(a) It reveals ground, never distance.** A personal light that extended sight range would be the
§6.2 reversal being repealed by a convenience — the dark would stop costing anything and the
player would simply switch the night off. It is presentation only: it illuminates what is already
near and does not move `PlayerSightCurve` by a millimetre, which is enforced structurally (no
`AppendLitLightSamples` exists on it to call) and by a scanner test with a positive control, not by
a comment. **(b) It does not compete with the fire.** Canon fact 3 reserves 1.0 intensity for the
campfire and `GlowStickProfile` states the personal backstops as asserted numbers; this sits under
both, and its colour is deliberately cool (§6.6) so it can never be mistaken for warmth.

| Device | Status in this level | Note |
|---|---|---|
| Night reversal | **leaned AGAINST for the first time in this ledger, and said out loud** | Six §12 entries in a row have recorded "leaned on, not resolved" while touching nothing. This one hands the player an instrument that works *against* the reversal — the first thing in the repo that does. It still does not move `OutdoorAtmosphere.NightAmbientFloor` and still does not resolve §6.2, and the distinction is the whole of the defence: the floor is what the dark costs **everyone, everywhere, always**, and a tool is what one player spends an input to hold for as long as they choose. Whoever holds the fork should read this row as *evidence*, not as a decision: the harness now contains a way to stand in the dark with a light, and what he reports about that is worth more than another argument about the constant. |
| Cost-to-know (§2.3) | **NOT spent, and its absence is the finding** | §2.3 is the strongest single device in the researched corpus and this light is its exact inverse: it reduces uncertainty and costs **nothing** — no battery, no fuel, no consumable, no noise, no attention. Every other light in this repo runs out (the stick is consumable, the torch burns down, the fire eats wood); this is the first that does not. That is correct for a playtest whose stated subject is movement and exploration and whose owner asked for "simple", and it is a live §8.7 failure the moment the night has anything in it. Recorded as the §13 fork below rather than resolved, because the shape of the cost is a design call and inventing one to satisfy a gate would be the director picking a value. |
| Colour temperature as threat channel | **borrowed, not re-spent** | Cool white, chosen so the fire keeps warm to itself (canon fact 3). No new statement in the channel; the direction of the shift is the one this file already recorded. |
| Day/night cycle as clock | **borrowed, not re-spent** | Unchanged from every prior Bubble Test entry. Nothing here authors a clock, and the flashlight is deliberately not one — a battery would BE a clock, which is part of why the fork below is a fork. |
| The unreadable dark | **not drawn on, and must not be** | That row's deposit is that nothing in the dark ever resolves. A five-metre pool resolves what is *inside* it and nothing beyond, so the deposit is untouched. It would be spent the moment the light were made to sometimes reveal something — which is a beat, not a lamp, and is not this. |

**The five, at this altitude.** *Belief:* "there is nothing I can do about the dark" — which the
build actively taught, because there genuinely was nothing. *Break:* the first press. *Why here:*
it is not a beat and has no position in a rhythm; the honest answer is "because the only tool in
the game for the dark was a consumable you throw on the floor and walk away from, and he had none
of them". *Who perceives it:* every peer, in the same instant — the state is server-arbitrated and
broadcast, and a second client photographing the first client's glow is committed evidence
(`docs/qa/night2/peer/`). That matters here beyond correctness: §5.4 makes divergent perception a
dread multiplier, and a light one player can see and another cannot would have been divergence by
**bug**, which reads as broken rather than as frightening. *What changes:* the player owns a
radius. Nothing else about the night moves.

**Dread gate.** *Ambiguity §2.1* — **PASS, narrowly and by construction.** The all-clear does not
arrive: the pool ends, and what it mostly reveals is where it stops. It would FAIL immediately if
the light granted sight range, which is exactly why constraint (a) is constitutive. *The clock
§2.2* — **FAIL**, correctly and in the standing words: the Bubble Test has no pressure driver,
nothing approaches, and it must never grow one to pass a gate. *Break §3.1* — **FAIL**; nothing
builds, so nothing breaks. *Incompleteness §3.2* — **n/a**; there is no build to release.
*Degradation §3.4* — **FAIL, and this is the real one.** The light never runs out, never dims and
never costs anything, so the safety it provides does not degrade. Named rather than tuned; see the
fork. *Affect §6.0* — **PASS**. Perceivable (a frame either side, committed) and it lands: the
frame with the light on has a floor, a body and a bench in it, and the frame without has a wash.
*Fairness §7.1/§7.2* — **PASS**. The rule is one key, stated in the one place the player is looking
at the moment it becomes useful, and it behaves identically every press.

**Spike gate — no spike claimed**, and none is available: a toggle with no stake and no violation
fails conditions 1 and 2 before simultaneity is even asked.

**Prohibitions, all nine.** *8.1 over-exposure* — n/a; nothing is exposed, and note that the light
reveals **ground**, never a creature at range, so it cannot become a threat-categorising
instrument. *8.2 startle as build* — not violated: the light comes up instantly but it is
player-initiated, and a state the player asked for one frame earlier is not a startle. *8.3
reliable telegraphs* — n/a; it precedes nothing and predicts nothing. *8.4 scripted set-pieces* —
not violated; it fires when the player presses a key and never on a schedule. *8.5 illegible
chaos* — in service of it, in the plainest possible way. *8.6 unrelenting tension* — n/a. *8.7
permanent safety* — **VIOLATED, knowingly, and it is the fork below.** A free, infinite,
instantaneous, portable light is a permanent sanctuary in the same class the *blind hide* row
already warned about, and it is a worse case than that one because it costs not even a held
button. It is tolerable **only** while the night contains nothing, which is true of the Bubble Test
today and false of the game. *8.8 streamer-first* — not violated; downstream of a player complaint.
*8.9 punitive failure* — n/a.

**Handed off, unanswered — implementation, not direction:** the radius, the intensity and the
colour (values, picked and stated by the packet, not by this file); whether the glow should have a
sound; whether the toast is the right and only place the key is taught; and **which of §6.2's sides
ships**, which is Talon's and is untouched here.

**Five doorways instead of one — directed 2026-08-29 (LEVEL-4, the Bubble Test level).**
Altitude: **scene**, and specifically a *re-spend* of a scene already directed rather than a new
one. Talon's note 9 asked for "a few more TVs" with "a slightly different room" behind each, so
BT-10's one room behind two televisions became five rooms behind five. The question this pass
exists to answer is not what the room should feel like — BT-10 decided that and this pass changed
none of it — but whether **doing it five times is still the device**.

**The ruling: it is not, and that is accepted rather than argued away.** *The object that is a
door* runs on a prop turning out to be a route. The first television is a violation. The fifth is
a **category**: the level now teaches, by its fourth screen, that televisions are how you get
places here — and a categorised wrongness is a mechanic, which is §8.1 exactly. So the row's state
for this world moves from `spent-here` to **`burned` in `BubbleTest`**, with two qualifications
that a later director must read together:

- **BT-10's decay note still governs game-wide.** A harness spend is not a game spend; this is a
  movement/networking level, not a level of the game, and the device is **still available and
  still whole outside `BubbleTest`**. Nothing here retires it.
- **But the §13 fork "whether the game proper may contain a place its map does not explain" now
  has a worked example of its own side (iii) — and it arrived exactly the way that fork warned it
  would, by inaction.** The fork says: *"portals are cheap to add and each one individually seems
  fine."* Five of them were added in one packet, each individually fine, at a designer's explicit
  request, and the result is a level where off-map rooms are a genre convention. **That is
  evidence for the fork, not a resolution of it, and a later director must not read this harness
  as permission.** The fork stays open and its trigger is unchanged.

**The four BT-10 constraints, all four re-checked against all five rooms, all four hold.**
(a) *Leavable at will and immediately* — every room has its own return television 2.2 m from where
its traveller lands, and `--tvportal-selftest` now drives a real body through all five round trips
rather than one, which is what turns this from an assertion into a measurement. (b) *Out of frame
on arrival, unmissable after one turn* — every entrance faces south, every arrival lands facing
into its room with the way out behind, so §7.2's one-rule consistency is preserved across five
doors instead of being true at one and accidental at the rest. (c) *Nothing animates in response
to arrival* — no new room has a flicker, a trigger or a moving prop; the only motion in any of
them is a television's static, which was already running. (d) *Local to the traveller* — the
transition is unchanged and still reaches one peer.

**RoomD's lampless variant — allowed, and it is the edge of an existing constraint rather than a
new device.** The room with no lamp stand leans directly on this row's own "may be the brightest
thing in the room and **must not become the room's light source**". Its dim fill is what keeps it
legal: the room reads as lit by something, and the screen is merely the brightest thing in it.
**Lowering that fill any further would spend *the unreadable dark*** — a row this ledger records
as an unspent deposit — and it must not be spent by a lighting tweak in a test harness. Recorded
so the next person who thinks "this room would be better darker" finds out why not first.

**Correction, same day, from the headed capture — because the sentence above was written before
anyone looked.** This entry first said RoomD's only light was "the television and a dim fill".
That is false, and the capture shows it: **a golden cube is the brightest object in that room.**
`Crate.tscn` ships `emission_energy_multiplier = 2.5`, and note 12 put one carryable in every
room, so RoomD contains a hand-portable lamp brighter than its own television. Three things follow
and none of them is a defect: the room is *more* legible than the direction assumed, not less; the
TV-as-light-source constraint is untouched (the TV is not the room's light source — but neither is
it the brightest thing, which is a weaker claim than this row made); and **a light a player can
pick up and carry into a dark room is a device this register does not have a row for.** It is not
claimed as a spend here — it arrived as a side effect of a networking fixture, in a harness, and
inventing a row for an accident would be exactly the register inflation §10 warns about. It is
recorded because the next director who wants a portable light source should know the engine
already produced one by accident and what it looked like.

**Dread gate.** *Ambiguity* (§2.1) — **pass, weakened.** The withheld all-clear is intact per room
(you are returned, nothing acknowledges the trip, the prop is still doing what it was doing), but
the level-wide ambiguity is gone: by the third screen the player knows what a screen does. *Clock*
(§2.2) — **fail, and correctly so.** There is no clock here and there is not meant to be; this is
atmosphere in a harness, and saying so in those words is what §2.2 requires. *Break* (§3.1) —
**pass on first use, fail thereafter.** *Incompleteness* (§3.2) — **pass.** *Degradation* (§3.4) —
**n/a**, no safety is modelled. *Affect* (§6.0) — **pass**: the rooms differ enough to read as
different places rather than as one room re-lit, which is the whole of what note 9 asked for.
*Fairness* (§7.1, §7.2) — **pass, measured**: five round trips, no room a player can be held in.

**Spike gate — no spike claimed.** §4.4 says most beats must not be one, and multiplying a
creepiness device five times is the opposite of a spike.

**Prohibitions.** *8.1 over-exposure* — **violated, knowingly, in a harness, at Talon's explicit
request; the row is marked `burned` here as the price.* *8.2 startle as build* — not violated;
constraint (c) is what prevents it and it holds in all five. *8.3 reliable telegraphs* — **now
violated in the weak sense**: a television reliably predicts a room. In a harness there is nothing
to relax about, so it costs nothing here and would cost everything in a level. *8.4 scripted
set-pieces* — the rooms are static by construction; nothing fires. *8.5 illegible chaos* — not
violated; each room is legible on arrival. *8.6 unrelenting tension* — n/a. *8.7 permanent
safety* — n/a. *8.8 streamer-first* — not violated. *8.9 punitive failure* — not violated, and
this is the one that was actively defended: a room with no way out is the same defect as note 4's
loss screen, and it is now tested rather than trusted.

**The golden cubes are not a directorial spend and are recorded so nobody later thinks they
were.** Twenty carryables scattered across five sections and five rooms are a networking test
fixture Talon asked for by name. They place no creature, author no clock, withdraw no sound and
grant nothing. The only directorial note is that one sits in each room, which gives a traveller a
reason to look around a room whose whole point is that nothing in it moves.

**Canon updated:** this entry; *The object that is a door* → `burned` in `BubbleTest`, unchanged
game-wide; §13's off-map-place fork annotated with this pass as evidence for side (iii), not
resolved.

**Handed off, unanswered — implementation, not direction:** every palette value in the four new
rooms (art-atmosphere's, and the director picks none of them); whether RoomD's fill energy is the
right amount of "lit by something", which is a look call nobody has seen yet; and whether the game
proper ever gets an off-map place at all, which is §13's and Talon's.

**Four doorways moved onto summits — directed 2026-08-30 (W7-1, the Bubble Test level).**
Altitude: **level**, and the altitude matters because it is what the pass actually changes. Talon's
playtest note 8 — *"keep one of the 'TV sets' on the gray level… put the rest of the TVs at the
top, in the hard to reach places of the world around. Please make these the reward at the end"* —
moves nothing about what a room feels like. BT-10 decided that; LEVEL-4 multiplied it; W7-1 changes
only **where in the session's arc the four doors sit**, which is §3.5 rhythm and nothing else. A
`scene` direction here would be answering a question this pass does not ask.

**Feeling.** *You paid for this, and it does not care.* The four relocated televisions sit at
22.810 m (red's cairn summit), 34.782 m (a dead-end spur off the new tangle spire), 41.200 m (the
blue tower's summit) and 60.789 m (the spire's cap, 59 single jumps above the plaza). Walking into
one still produces exactly BT-10's beat — a sealed room, nothing acknowledging the trip, the prop
still sitting there doing what it was doing — but the player now arrives at it having spent
something real to get there. **The reward and the withholding are the same object**, and that is
the whole of what W7-1 adds.

**The ruling, and it is a refusal.** *The object that is a door* is **`burned` in `BubbleTest`**
and W7-1 does not un-burn it. Moving a categorised wrongness uphill does not de-categorise it: by
the fourth screen this level still teaches that televisions are how you get places here, and
altitude is not an answer to §8.1. **This pass claims no dread and must not be recorded as having
produced any.** What it genuinely adds is on two other axes, and naming them precisely is the point
of this entry:

- **§4.1's stake and §4.5's agency, for the first time in this world.** Every previous television
  stood on flat ground and cost a walk. These cost a climb the player chose to make, and the
  spur's four blocks cost a detour off the climb — a decision with a price, which is exactly the
  shape §4.5 requires and which nothing in `BubbleTest` had before.
- **§3.2's incompleteness, arrived at for free.** The release (you reached the top) is real; the
  room resolves nothing and returns you to the same pad with the descent still ahead of you. That
  is the incomplete release doctrine asks for, and here it costs no authoring at all.

**No new device row, deliberately.** "Put the prize at the top of the climb" is the oldest idea in
level design and registering it would inflate a ledger whose value is that it only holds dread
techniques. Checked against the four nearest rows and all four miss it in the other direction:
*the object that is a door* is about the prop, not its altitude; *the bounded exception* is about a
world-rule broken in a room with an edge; *the apparent artefact* is about regularity with no
maker; *cost-to-know* trades safety for information and a climb trades neither. The right record is
an amendment to the existing row, which is what was written.

**A FIFTH CONSTRAINT, and it is new.** BT-10's four constraints on *the object that is a door* were
written for doors at ground level, and one of them silently stops meaning anything at altitude:

> **(e) The way out must return the traveller ONTO the surface they left, not below it.**
> Constraint (a) says the destination must be leavable at will and immediately. At 41 m, a return
> that lands a player one metre outside the pad is still technically "leavable" and is in fact a
> 41 m fall used as an exit — the beat becomes a punishment for having taken it, which is §8.9,
> and the player who climbed for it reads the game as taking the climb back.

**This was live, not hypothetical.** `RoomReturnOffset` shipped at (3, 1, 3) — a 4.2 m diagonal
sized for a room's floor — and against a 5 m summit pad it puts the returning player **3 m past
the edge** on three of the four new destinations. W7-1 cut it to (1.4, 1, 1.4) for exactly this
reason and for no aesthetic one. It is now measured rather than reasoned: `--tvportal-selftest`
drives a real body through all five round trips and every returning body settles at
**22.810 / 34.782 / 41.200 / 60.789** — its own pad's top, to the millimetre — instead of falling.

**The four BT-10 constraints, re-checked at altitude, all four hold.** (a) *Leavable at will and
immediately* — every room keeps its own return television 2.2 m from where its traveller lands;
unchanged, and now guarded at the far end by (e). (b) *Out of frame on arrival, unmissable after
one turn* — unchanged: every entrance still faces south and every arrival still lands facing into
its room. (c) *Nothing animates in response to arrival* — unchanged; no room was touched. (d)
*Local to the traveller* — unchanged.

**Dread gate, run honestly and mostly failing.** *Ambiguity* (§2.1) — **fail, and it failed before
this pass.** The all-clear is withheld per room, but five doors in one level answered "what is a
television here" a packet ago; a climb does not restore an ambiguity that is already spent.
*The clock* (§2.2) — **fail.** There is no clock on a climb. `BubbleTest` has the day/night cycle
running and nothing in this pass leans on it; the spire is atmosphere with a payoff, not dread, and
saying otherwise would be the exact sentence §2.2 exists to refuse. *Break* (§3.1) — **pass,
weakly**: the summit is a break in a climb's build. *Incompleteness* (§3.2) — **pass**, per above.
*Degradation* (§3.4) — **n/a**; no safety is modelled in this world. *Affect* (§6.0) — **pass**:
the goal-material cap is visible from the plaza, so the prize is perceivable from the bottom of the
climb, which is what makes starting it a choice rather than a wander. *Fairness* (§7.1, §7.2) —
**pass, and this one was worked for.** Every step on the spire is a pure vertical step-up of at
most 0.946 m against `MotorArc.HeldSprint`'s 1.407 m apex, with consecutive blocks proved to
overlap in plan pair by pair, so there is no gap to clear anywhere and the air jump is required
nowhere; a whole-level reachability search reaches all four pads on **single jumps alone**. A climb
that needs a jump the player does not have is §7.2's arbitrariness wearing a level's clothes.

**Spike gate — no spike claimed**, and none is authored. One is now *possible* for the first time
in this world and it is worth recording as an observation rather than a spend: a player falling
60 m off the spire in front of a teammate has stake (the climb), violation, simultaneity and
legibility, and it is downstream of their own choice to push higher. **Nothing in W7-1 authors,
tunes or aims at that** — it falls out of the geometry, the fall is non-lethal, and a later
director must not read this paragraph as a spend.

**Prohibitions, all nine.** *8.1 over-exposure* — **still violated, still knowingly, still in a
harness**, and this pass neither worsens nor repairs it; the row stays `burned` here. *8.2 startle
as build* — not violated; constraint (c) is untouched. *8.3 reliable telegraphs* — **weakly
violated in a second way, and it costs nothing here**: a goal-coloured cap now reliably predicts a
television. In a level where a lit summit could ever be a lie that would be a real spend; in a
harness with nothing to relax about it is legibility, which is what §6.0 wants. *8.4 scripted
set-pieces* — nothing fires. *8.5 illegible chaos* — not violated; the prize is legible from the
plaza and the route to it is the pile itself. *8.6 unrelenting tension* — n/a. *8.7 permanent
safety* — n/a. *8.8 streamer-first* — not violated; the spire was asked for by the player of the
build, for his own play. *8.9 punitive failure* — **actively defended, and it is the reason
constraint (e) exists.** A return that dropped a climber off his own summit is precisely "we wasted
an hour", and it is now tested rather than trusted.

**§13's off-map-place fork is NOT further evidence, and the distinction matters.** LEVEL-4 added
five doors and that was the evidence. W7-1 adds none — it moves four that already existed. A later
director counting portals must not count this pass twice.

**Canon updated:** this entry; *The object that is a door* amended with constraint (e) and with the
W7-1 relocation, its `burned` state in `BubbleTest` unchanged and its game-wide availability
unchanged; §13 untouched.

**Handed off, unanswered — implementation, not direction:** whether the tangle's goal magenta is
the right loudness for an 8 m cap seen from 130 m away (art-atmosphere's, and it is a look call
Talon has not seen yet — his note 7 about the golden cubes being "overblown" is the reason it is
named); how many jumps a climb should be before the payoff stops being worth it, which is a
playtest number and not a directorial one; and whether the spire should carry bubbles of its own,
which is a `BubbleTarget` question for whoever owns D8's split.

**The glow of a findable — directed 2026-08-30 (W7-3, Talon's playtest note 7, Bubble Test).**

Talon, verbatim, having played the level for the first time: *"The golden cubes, in the day, are
overblown like too bright."* **Altitude: `system`.** Not `scene` — this is not one moment, it is a
property of twenty objects across every section and every room, and it behaves differently in
daylight and in the dark. Not `level` — the Bubble Test's composition is not in question and the
harness-is-not-a-game ruling still governs. What is in question is what a *self-lit collectible* is
allowed to be at all, which is a system.

**This entry is the sequel to the correction three paragraphs up, and it exists because that
correction ended without a ruling.** The 2026-08-29 capture found that `Crate.tscn`'s
`emission_energy_multiplier = 2.5` makes a golden cube **the brightest object in RoomD, brighter
than that room's own television**, and recorded — correctly — that *"a light a player can pick up
and carry into a dark room is a device this register does not have a row for"*, declining to invent
one for an accident. It is no longer an accident: a director is now ruling on it, and a ruling that
leaves the accident in place is the same as authoring it.

**Feeling.** In daylight, a findable must read as **worth crossing the map for** — desire, not
glare. At 2.5 it reads as neither: an object blown past its own silhouette reads as a *rendering
fault*, and nobody wants a thing that looks broken. In the dark, the cube must read as **something
is there**, at rest, in a place — an announcement of presence, and nothing beyond it.

**The five.** *Belief*: the world is lit by the world — the sky by day, the lamps and screens
below. *Break*: a hand-portable object out-lights the room it is carried into. *Why here*: because
this is the first object in the repo that a player can pick up and see by, and the precedent is set
the first time rather than the fifth. *Who perceives it*: everyone, continuously — this is not a
beat, which is exactly why it is a system-altitude call. *What changes*: the cube stops being the
level's accidental lamp and goes back to being its reward.

**THE RULING, and it is one sentence: the cube may GLOW; it may not LIGHT.** It may be the
brightest thing in view. It must never be the thing by which the player sees. The instant its light
lands on the floor and the walls, it has stopped being a findable and become a lamp — and a free,
permanent, never-expiring lamp is the **camp glow stick's job taken away without the glow stick's
price.** That row's own condition is explicit and this is the case it was written against:
*"The moment a stick is free, permanent, or visible from camp, this flips to `defused` and both
woods devices go with it."* A carryable that lights the dark is free, permanent, and does not burn
out. In a harness that costs nothing. In the game it would defuse the glow stick, and `Canopy as
spatial darkness` and `Landform as disorientation` go down with it, because the glow stick is the
counterplay that keeps those two fair.

**Two consequences the implementer may act on without coming back.** (a) Lowering the emission is
the *whole* fix and needs **no second clock** — the crate carries emission and no light node, so
one number is simultaneously the daylight correction and the dark constraint; a day-aware emission
driver would be a second lighting authority for a crate and is refused, not deferred. (b) The value
itself is **not the director's** and is handed off below.

**One flag, not a change.** §6.6 makes colour temperature a threat channel, and warm reads as life
and intimacy — which in this game is the campfire's monopoly (canon fact 3: the fire is the one
warm, alive place in a cold dark world). A warm gold point, steady, at distance, is borrowing that
colour. In a harness it costs nothing and the cubes are *called* golden, so **nothing changes here**
— but the first played level that scatters warm self-lit findables across a night is putting a
second warm point-source in the fire's lane, and that is a §6.6 collision to resolve **before** it
ships, not after.

**Dread gate.** *Ambiguity* (§2.1) — **fail, and correctly**: a collectible is not ambiguous and is
not meant to be. This is a legibility system, not a dread system; the direction's whole job is to
stop it *spending* ambiguity that belongs elsewhere. *Clock* (§2.2) — **fail**: there is no clock
here and there is not meant to be. Atmosphere in a harness, said in those words as §2.2 requires.
*Break* (§3.1) — **n/a**, no build to break. *Incompleteness* (§3.2) — **n/a**. *Degradation*
(§3.4) — **fail, and this is the one that bites**: the glow never dims, never expires and costs
nothing, so in a played level it is navigability that does not degrade. In the harness, n/a; the
ruling above is what keeps it n/a later. *Affect* (§6.0) — **FAIL as shipped, and this is exactly
Talon's note**: the cue is perceived and what it feels like is *"overblown"* — it reads as an error
rather than as a reward. A cue that is perceived and feels wrong is a §6 defect, which is this
file's jurisdiction and not `LEVEL-BIBLE.md` §8's. *Fairness* (§7.1, §7.2) — **pass**: nothing here
can kill anyone or change its rules.

**Spike gate — no spike claimed.** §4.4: most beats must not be one, and a pickup's shader is not
a candidate.

**Prohibitions, all nine.** *8.1 over-exposure* — not violated; no threat is shown by any of this.
*8.2 startle as build* — not violated, and structurally cannot be: a steady glow is the opposite of
a spike, and it must stay steady for that reason. *8.3 reliable telegraphs* — **not violated, with
a guardrail that is the price of staying that way**: the glow must remain a property of the OBJECT,
identical in every room and at every hour. The moment it brightens on approach, pulses in a room
that matters, or differs between a cube indoors and a cube in the open, it has become a telegraph
and would spend the reserved **Flash beacon** row sideways — that row is a *brief, ambiguous* light
event, and it already warns that "a light that stays on stops being an ambiguous event and becomes
a findable waypoint". A findable waypoint is precisely what a cube is; the two must never share a
visual language, which they currently do not and must not be made to. *8.4 scripted set-pieces* —
not violated; nothing fires. *8.5 illegible chaos* — not violated, and improved: the fix makes the
object more legible, which is the point. *8.6 unrelenting tension* — n/a. *8.7 permanent safety* —
**at risk, and the ruling is the mitigation**: a free carryable light that never expires is
permanent navigability. Glow, never illuminate. *8.8 streamer-first* — **actively relevant, and it
is the reason this is not merely a taste fix.** BT-9b already recorded that a bright frame is "a
store-page asset in a way a near-black wood was not"; an object blown out past its silhouette is
that same instinct arriving through a material. Bringing it down is the anti-8.8 move. *8.9
punitive failure* — n/a.

**Canon updated:** this entry; the **Flash beacon** row's guardrail restated here against a
carryable rather than a one-shot (no new row — a *refusal* is not a device, and inventing a row for
one is exactly the register inflation §10 warns against); the **Camp glow stick** entry's
`defused` trip-wire recorded as having been tested and held; §13 gains the findable-self-light
fork, unripe, with its trigger.

**Handed off, unanswered — implementation, not direction:** the emission value itself, in both
lights (art-atmosphere's, and the director picks none of them); whether the corrected cube still
announces itself at the far end of RoomD, which is a look call and must be *photographed* rather
than argued; and whether the game proper's findables are self-lit at all, which is §13's and
Talon's.

## 13. Open forks

| Fork | § | Ripe? | Trigger |
|---|---|---|---|
| ~~Tone position~~ → **axis decided 2026-07-26**; only the dread/absurd *ratio* remains | 9 | **No** | first playtest where anyone is frightened at all |
| Night floor vs. night reversal | 6.2 | **RIPE — trigger fired 2026-07-26** | Fired: Talon played hoodlab and reported the dark as creepy and effective, then asked for it to swallow the house and to be un-outrunnable — a threat, not a palette. What is *still* undecided is narrow and now askable: he stood in darkness **level 1 only** (the shipped §6d#25 navigability floor). The remaining question is which of the three levels ships, and it needs one side-by-side session, not an argument. The director does not pick it. **Amended 2026-08-07 (#152) — the fork's shape changed, its answer did not.** Until now the only darkness was *scheduled*: `MinAmbientEnergy` was the sole thing standing between the player and an unnavigable night, so the fork was genuinely binary — raise the floor or accept frustration. A player-lit campfire introduces a **second, player-controlled** source of navigability, which splits the question the floor was answering. It now has at least three sides, not two: (a) the floor stays as shipped and firelight is a comfort on top of it; (b) the floor drops *because* the fire can carry navigability, making the dark cost sight exactly as §6.2 wants — at the price that a group who never lights the fire is navigating by nothing; (c) the floor becomes conditional on the fire, which buys the reversal but hands §7.2 a rule that changes state, and §7.2 is not subtle about that. **#152 deliberately picks none of these and must not be read as having picked (a) by inheritance** — it leaves `MinAmbientEnergy` untouched precisely so all three stay reachable. Ripeness is unchanged and the trigger is still the one side-by-side session, but that session should now be run *with* a lightable fire in it, because testing the three darkness levels against a camp that has no controllable light answers a question the game no longer asks. **Amended again 2026-08-08 (W5, the lake) — a fourth side, and a better venue. Still not resolved, and `MinAmbientEnergy` is still untouched.** Every venue so far framed this as a question about how well you can *navigate*. The lake is the first place where the floor has a direct visible cost to a **directed feeling**, in both directions: ambient energy lifts a depth-tinted water surface off black, so the floor is a straight subtraction from the *unreadable dark* deposit; and dropping the floor costs something the woods never risked, because a swimmer who cannot find the shoreline is a §7.4 problem rather than an atmosphere one — water is the only place in the camp where being unable to see costs you the ability to **leave**. So, four sides: **(a)** floor stays as shipped and the water is graded to work under it — safe, and the lake becomes the clearest evidence in the game that the dark is a palette; **(b)** floor drops and the fire plus the shoreline carry navigability — the reversal gets its substance where it is most visible, at the price that a group who never lights the fire navigates by nothing; **(c)** floor conditional on the fire — buys the reversal honestly, hands §7.2 a rule that changes state; **(d) new** — floor stays global but the water *material* is exempted from ambient lift, which keeps the deposit and full navigability at the price of a small lie about how light works, and needs `ART-BIBLE.md` §4.4 to have an opinion it does not yet have. **Ripeness unchanged — still one side-by-side session, not an argument. What W5 contributes is where to hold it: stand on the DOCK, not in the woods.** The three darkness levels are hard to tell apart among trees and trivial to tell apart over open water, because the lake's black is the only surface on the map whose value is almost entirely a function of the floor. |
| Whether the game proper's findables are self-lit at all | 2.3, 3.4, 6.6, 8.7 | **No** | Added 2026-08-30 by the findable-glow pass (W7-3). `Crate.tscn` glows because a networking fixture needed to be findable, and the 2026-08-29 capture found it out-lighting a television. That pass rules **glow, never light**, which is enough for a harness and does not answer the level question. **Three sides, and side (i) is the one that arrives by inaction.** (i) *Findables glow* — cheap, immediately legible, and it quietly hands every player a free permanent light source; the **Camp glow stick** row says in as many words that a free, permanent light defuses it, and `Canopy as spatial darkness` and `Landform as disorientation` go with it, because the stick is their counterplay. (ii) *Findables do not glow, and finding them is the game* — which puts the whole burden on the level's own light and on §2.3's cost-to-know (you carry a light you paid for, or you do not find them), and is the version most consistent with canon fact 2. (iii) *Findables glow only where the level already grants sight* — indoors, in lit rooms, by day — which buys legibility without buying navigability, at the price of §7.2: a rule that changes with location is exactly what §7.2 says players read as broken rather than as mysterious. **The director does not pick.** *Ripeness trigger: the first non-harness level that places a findable in the dark.* Until then this is a question about twenty cubes in a test fixture, and the answer would be tuned against a night nobody has had to search. |
| Warning lead time vs. point-of-no-return | 7.1, 8.3 | **No** | the first time being caught by the dark costs anything. Today the golden hour begins at the same instant the front starts moving, so there is no lead time — and the safe-return radius is exactly sprint speed × sweep duration (145.8 m at the shipped numbers), which makes every metre beyond it a point of no return the moment the sweep starts. Harmless while the dome only changes the lighting; a fairness problem (§7.1) the instant it does not. Three sides when it ripens: warn before the sweep, let the far ring be a genuine no-return zone, or give outlying zones their own local cue. |
| One clock or two (tide + cycle) | 2.2 | **No** | the tide is back in a level alongside the cycle |
| Spike ceiling per session | 4.4 | **No** | a session has produced two spikes and one felt cheap |
| How hard the land is allowed to fight the player | 6.1, 7.1 | **No** | Added 2026-08-07 by the terrain-and-forest pass. **Three sides, not two:** (i) *land as texture* — you always arrive where you meant to, it merely looks like somewhere; (ii) *land as friction* — it costs time and effort but never direction; (iii) *land as agent* — off-trail it takes direction away from you and the way back is not the way you think. This pass builds (ii) leaning toward (iii), because that is what pairs with the canopy's withdrawal of the beacon. It is a **degree** question, which the director does not resolve, and it cannot be argued to a conclusion — the same amplitude reads as "I couldn't tell the terrain was there" or "I kept getting stuck" depending on nothing but how it feels underfoot. *Ripeness trigger: the first session in which anyone walks the woods off-trail and reports one of those two sentences.* Until then, build (ii)→(iii) and change it on a report, not on an argument. |
| Whether the night lake's affect may depend on anyone entering the water | 1, 2.1, 6.0 | **No** | Added 2026-08-08 by the lake pass (W5). The water contract §1.1 makes the night lake deliberately empty — no evidence, no objective, nothing to swim for — which means the swim's genuine build-and-break (chill → go-under) is real and almost entirely **unreachable**, and the register has to live on the shore instead. **Three sides:** (i) *shore-only, permanently* — the water is a surface and never a place, and the dock carries the whole register for the life of the game; (ii) *shore-first* — a surface now, with a reason to enter arriving when the camcorder loop and creature sim can say what is worth reaching (this is what §1.1 currently **implies**, but implying is not deciding); (iii) *the shore grows a build of its own* that needs no entry at all. W5 builds (i) as the honest present state while leaving (ii) reachable, which is what §1.1 requires. *Ripeness trigger: the first five recorded night sessions. If nobody swims at night in any of them, side (i) has answered itself and the swim register can stop being budgeted for.* |
| How often the same player may be watched | 8.1, 8.4, 7.2 | **No** | Added 2026-08-09 by the watcher pass (R3). §8.1 wants the sighting vanishingly rare; §7.2 wants its rule *learnable*, and a rule nobody sees twice is never learned; §8.4 wants the frequency to come from the possibility space rather than from a schedule. Those three pull in three directions and no argument settles them. **Three sides:** (i) *once per run* — maximum §8.1 protection, and the doe run is never practised by anybody, so the second device row is decorative; (ii) *once per night* — the rule becomes learnable across a five-night arc, at the price of the sighting becoming a thing that *happens on schedule*, which is §8.4's set-piece arriving by the back door; (iii) *as often as the players' own visibility invites it*, with rarity emerging from behaviour rather than from a budget — the §8.4-correct answer and the §8.1-dangerous one, since a group that keeps standing up will meet it repeatedly and categorise it by night two. R3 builds (iii) because it is the only side that falls out of the scoring rather than being imposed on top of it, and because it is the one side that can be turned into either of the others later by adding a budget, whereas (i) and (ii) cannot be turned into it without a redesign. *Ripeness trigger: the first session in which one player is watched more than once and reports either "I stopped caring" or "I never worked out what it wanted." Those two sentences point at opposite sides and nothing else distinguishes them.* |

| What makes the blind hide finite | 3.4, 8.7 | **No** | Added 2026-08-09 by the panic-drop pass (R4). Today the only thing bounding a prone player's safety is that holding a button gets tiring, and R4's own brief names toggle-after-threshold as the fallback if playtest agrees it is too tiring — which would remove the bound entirely and leave the game's first **portable** permanently-safe sanctuary (§8.7, and see the §12 entry). **Four sides, not two:** (i) *nothing* — the hide is unbounded and safety is simply free once you are down, which is honest and is what the toggle fallback ships by default if nobody decides otherwise; (ii) *the body* — staying down costs something the player carries (cold, cramp, a slower recovery the longer you were down), so the exit gets more expensive the more you used it, which is §3.4 degradation in its purest form and needs no new system; (iii) *the world* — the errand's own clock does the bounding, because the fire is burning down while you lie there and nobody else can carry your wood, which costs nothing to build because the clock already exists; (iv) *the threat* — the watcher eventually comes to look, which is the most obvious answer and the worst one, because it converts the only honest defence in the game into a timer the player cannot read and hands §7.2 a rule that changes without a cause the player can learn. **The director does not pick, and notes only that (iii) is already paid for and (iv) should be argued for very hard before it is built.** *Ripeness trigger: the first session in which anybody stays down longer than the walk itself takes — i.e. the first time hiding is used as a strategy rather than as a reaction. Until that happens the question is theoretical, and the tiring thumb is doing an adequate job of hiding it.* |
| Whether the lit ration contracts *within* a night, or only between nights | 2.2, 3.4, 8.6 | **No** | Added 2026-08-09 by packet R5. The driver ships both motions: a large night-over-night step and a smaller within-night squeeze (each night gives up 45% of its reach between dusk and dawn). **Three sides, and the middle one is not a compromise.** (i) *Between nights only* — the light is a constant all night, which makes it a clean, glanceable statement about the run and nothing else; the night itself then has no motion of its own and the whole cue can be read once at nightfall and ignored. (ii) *Both, as built* — the run's length is read between nights and the night's own pressure is felt inside it, at the cost that the two readings are the same quantity and a player may not separate them. (iii) *Within nights only* — the light recovers each dusk, which deletes the device's defining property (it would reset, like everything else in this file) and is recorded only so the option is not silently dropped. R5 builds (ii) because the within-night motion is what stops nights 4 and 5 being static once their opening radius is small, and it is deliberately eased **late** so the squeeze lands on people who stayed out rather than on people who left on time. *Ripeness trigger: the first night played with the driver applied to the lighting, in which somebody either narrates the light closing in during the night, or does not notice it at all. Both answers settle it; an argument will not.* |

| Whether a fire's audible reach and its lit reach are the same number | 6.0, 8.1, 2.1 | **No** | Added 2026-08-09 by packet 1f. Plan §15 aligns the audible fire set with the impostor-glow set at one rank cutoff, and that alignment is doing real work — it is what keeps the fire cue redundant across two channels and out of `LEVEL-BIBLE.md` §8.1's way. But *rank* alignment and *reach* alignment are not the same claim, and the packet found that they cannot both be free: the audible cutoff falls out of a **voice** budget (how many continuous 3D voices exist after the five speakers and the one-shot pool are paid for) while the lit cutoff falls out of a **draw** budget, and there is no reason those two numbers should land in the same place. **Three sides, and the middle one is not a compromise.** (i) *Fused* — one number, the fire is one object, and the cheapest thing to reason about; the cost is that the tighter of the two budgets silently governs the other forever. (ii) *Audio reaches further than light* — you hear home before you can see it, which makes sound genuinely the primary channel the darkness law says it is, and gives the walk back a build (a sound that grows before a glow appears); the cost is that a fire you can hear and not see is a §8.1 edge case where audio briefly *is* the only cue. (iii) *Light reaches further than audio* — you see home before you hear it, which demotes the crackle from navigation to arrival and makes it an intimacy cue rather than a beacon; cheap, and it quietly gives up most of what §1.5 was for. *Ripeness trigger: the first night session in which somebody gets genuinely lost and then narrates how they found their way back. Whether the sentence contains "I saw the fire" or "I heard it" answers this and no argument will.* |

| Does a small light ward at all? | 2, 7.2 | **No** | Added 2026-08-11 by the glow-stick pass. Canon fact 2 says creatures are averse to light and light is the boundary between safe and unsafe; the Light Line direction says string lights and glow sticks grant sight with **no ward**. Both are defensible and together they hand §7.2 a rule that changes by light *type* — the exact line between dread and frustration. **Three sides:** (i) *sight only* — small lights never ward, and the object must teach that on sight (what the 2026-08-11 build assumes, and why the stick is acid yellow rather than firelight-coloured); (ii) *partial or brief ward* — a stick buys seconds, not safety, which keeps fact 2 literally true at the cost of a value nobody can pick yet; (iii) *ward that attracts* — it holds creatures off and draws their attention, which is §2.3's cost-to-know trade and the most interesting of the three, but it needs a creature with attention to draw. *Ripeness trigger: the first session in which a creature and a placed light exist at the same time.* Nothing can be tested before then and the director does not pick. **Amended the same day by Talon, and the fork narrows sharply.** He rejected the pass's assumption that small lights should stay out of the gameplay light field: *"I think glow bugs should absolutely be gameplay light. Why wouldn't it be?"* — and the objection was wrong on its own terms (it assumed per-entity replication, when this repo's own law is "replicate the input, derive the rest": a seeded fixed-step swarm derives identically on every client for free, and a cluster can carry one authoritative light point for many rendered bugs). He also set a standing direction: **"there should be lots of small sources of glow throughout the woods as bugs or sticks or flowers… but not so much to light the forest up."** Those two together create a real tension — many small gameplay lights would ward the woods and kill facts 2 and 5 — and the proposed resolution, **awaiting Talon**, is to stop conflating two things: **visibility light** (how far you can see; every glow contributes, server data, parity-bound) and **ward light** (what creatures avoid; the same field above a threshold only fires and rock rings reach). One field, one rule, a magnitude — so small lights fail to ward because they are small, not because of a per-light-type exception, which is what keeps §7.2 clean. If Talon takes that, side (i) is answered as a consequence rather than as a special case. |

| What makes the eel's charge cost anything | 3.4, 8.7, 2.2 | **No** | Added 2026-08-13 by the eel-kit pass (STORMVAULT-EEL-2). The charge is the first safety in this game that recovers automatically, on a timer, for free, forever, with the player doing nothing — and canon 14 deliberately restores it to **full** on respawn. §3.4 and §8.7 both fail on it, and it is the exact inverse of the glow stick, which this ledger records as the first safety that genuinely runs out and that the player bought. **The mechanism's own argument against decay is sound and is not being overruled:** a leak would punish you for not having been frightened recently, and it would make the chain threshold probabilistic where it is currently exact. So the question is not *should it decay* — it is *what, if anything, the charge costs*. **Four sides, and the first is a real answer rather than a null.** (i) *Nothing — and that is correct*: this is a comedy resource in a kit whose job is release, the cost is already paid in the §5.4 information it broadcasts on your body, and a defence that always works is fine for a thing that defends against nothing. This is what ships if nobody decides otherwise. (ii) *The body* — being charged costs something you carry (a tell that is louder the fuller you are is already in the mechanism; making it *cost* is one step further), so being full is a state you might want out of, which converts feeling 1 from a non-decision into a real one. (iii) *The world* — the cost is positional and social: standing near your party while full is the price, which is what the mechanism already produces and which needs no new system at all, only a night in which huddling matters. (iv) *The threat* — a creature is drawn by the discharge, which is the most obvious answer and the one canon 7 puts entirely out of this file's reach: it is a per-creature ruling, Talon's, and §9.1's response table is empty by construction. **The director does not pick, and notes only that (iii) is already paid for and (iv) is not this file's to propose.** *Ripeness trigger: the first session in which anybody either (a) never spends a charge for a whole night, or (b) spends one purely because it was free. Those two sentences point at opposite sides — the first says the resource has no pull, the second says it has no price — and nothing else distinguishes them.* Nothing can be tested before a party has played the kit. |

| What breaks the line | 8.3, 7.2, 2.1 | **No** | Added 2026-08-14 by the radio-cord pass (NIGHTJOBS-B). The cord's entire affect budget rests on the break, and the break's honesty rests on its *cause*, which is a creature capability this file may not grant (§0) and which no creature substrate exists to host — the repo has exactly one coded creature (the watcher, which only looks) and `EelBlastCreatureResponse.Table` is empty **by ruling**. **Three sides, and the middle one is not a compromise.** (i) *Only creatures cut lines* — maximum meaning per break, and it fails §8.3 outright: a dead line then reliably precedes a threat, players learn to relax while the line is live, and the cord has been turned into the most reliable alarm in the game by the group's own labour. (ii) *Creatures plus at least one indifferent cause* — weather, deadfall, a route you chose badly, an animal that wants nothing from you — which keeps §8.3 honest and keeps §2.1's ambiguity attached to the *cause* rather than only to the *location*, at the price of a second system and a second failure mode to tune. (iii) *Only indifferent causes* — perfectly fair, perfectly learnable, and the break never means anything, which spends §5.5's device on maintenance chores. **The director does not pick, and notes only that (i) is the default that arrives by inaction** — the first creature able to touch world objects will be given the line because it is the obvious thing to give it, and nobody will notice that §8.3 went with it. *Ripeness trigger: the first creature in the repo that can act on a world object at all. Until one exists the question cannot be tested and the answer cannot be built.* |
| Whether losing the line may ever cost the run | 8.9, 5.3, 3.4 | **No** | Added 2026-08-14 by the radio-cord pass (NIGHTJOBS-B), and it is the affect face of a scope question that is **Talon's, not this file's** — the Light Line fork asks whether the comm line and the territory light-line are one object; this row asks only what follows *emotionally* from each answer, and does not lean on either. Canon 14 already makes a light line a **respawn-chain edge**, so if the two are one object then a severed cord regresses respawn, and the sixth filing of "time and dignity, never progress" collides head-on with a shipped canon rule. **Three sides.** (i) *Separate objects* — the cord costs voice and nothing else, §8.9 is satisfied trivially, and the player is carrying and maintaining two things along the same walk, which is the version most likely to read as busywork. (ii) *One object, two functions* — the elegance Talon named (string lights on the comm wire), and the strongest possible version of *Infrastructure as the attack surface*, at the price that one cut takes your voice **and** your respawn chain in the same instant, which is the largest single punishment in the design and is exactly the shape §8.9 warns turns "we almost died" into "we wasted an hour." (iii) *One object, asymmetric damage* — the same line, but severance degrades the two functions differently (voice drops instantly, the respawn edge survives or degrades slowly), which preserves both the elegance and the §8.9 floor at the cost of a rule players must learn about a thing that looks like one thing. **The director does not pick and must not**, because side (ii) is a canon consequence rather than an affect preference. *Ripeness trigger: Talon's ruling on the Light Line scope fork. This row is unaskable before it and answers itself for two of the three sides after it.* |
| Where the night is carried, now that the menu no longer carries it | 8.8, 2.1, 9 | **No** | Added 2026-08-28 by the light title screen (MENU-1). The campfire menu said "horror" before the player pressed anything; the approved light frame says "summer", and per canon direction fact 1 that is defensible — a sincere day face is the belief the night breaks. **Three sides, not two:** (i) the menu is the setup and the night arrives in the trailer/store page; (ii) the menu is the setup and the night arrives in the first dusk, with nothing before it; (iii) the day/night contrast belongs *in* the front face somewhere other than the title screen — a second screen, a loading frame, the session summary. What is refused on all three is adding a hint to the title screen itself; a menu that winks is worse than one that does not speak. Trigger: the first time this build has a store page or a trailer cut, or the first playtest where someone reports being surprised by the night in a way that read as a bait rather than as a break. **INVERTED 2026-08-29 (MENU-2), not resolved.** Talon re-directed the title screen to night, so the menu now carries the night and the question has flipped: *where is the DAY carried?* Side (iii) is largely overtaken — the front face now shows the night, so a second front-face surface would be carrying the day rather than the night. Sides (i) and (ii) survive with their content swapped. A fourth side has appeared and is the cheapest: (iv) *the day is carried by the game itself and by nothing before it* — the first session opens in daylight, so the sincere day face arrives the moment the player is in it rather than on the way in. The director does not pick, and notes only that the fork has now reversed once without being answered, which is itself evidence the trigger is the right one and has not yet fired. |
| Whether the game proper may contain a place its map does not explain | 2.1, 4.3, 6.4 | **No** | Added 2026-08-28 by the TV-portal pass (BT-10). The TV room is the first location in this repo that is **not part of the world's geography** — no approach, no exterior, unreachable by walking, and re-findable only by using the prop again. In a harness that is free. In a level it collides with two canon rules at once: canon 8 (*the map remembers* — an off-map room has nothing to remember it with, and no persistence substrate exists to give it one) and canon 10 (*the mystery deepens, never resolves* — an unexplained room is the purest form of that, and also the easiest way to accumulate unexplained rooms until none of them means anything). **Three sides.** (i) *Never — every place is on the map*, which keeps the world coherent and spends this device permanently on a test level. (ii) *One, ever* — a single off-map place in the whole game, which is the version most likely to be remembered and the hardest to justify building. (iii) *A category* — off-map places are a thing this world does, which makes them a genre convention rather than a violation and, per §8.1, stops being strange around the third one. **The director does not pick**, and notes that (iii) arrives by inaction: portals are cheap to add and each one individually seems fine. *Ripeness trigger: the first non-harness level that wants one. Until a real level asks, the question is about a room nobody has to live with.* **Amended 2026-08-29 (LEVEL-4) — side (iii) now has a worked example, and it arrived exactly the way this row predicted.** Talon asked for more televisions and more rooms; five portals went in, each individually fine, and `BubbleTest` now has off-map places as a genre convention rather than as a violation. That is **evidence for the fork, not an answer to it**: a harness is precisely the place where (iii) is free, which is why it happened there first and why it must not be read as precedent. The trigger is unchanged. |

| Whether a harness's night may be kinder than the game's | 6.2, 7.1, 8.3 | **No** | Added 2026-08-29 by the darker-in-general pass (DARK-1b). The Bubble Test now renders night with a fixed 28 m sight range, while camp and playtest1 still fall to `PlayerSightCurve.DarkFloorM` = 3 m — the two worlds' nights are no longer the same night. That is defensible: an instrument that cannot be moved through measures nothing. It is also the world Talon looks at most often, which makes it the world that quietly sets his expectation of what "night" is. **Three sides.** (i) *The harness matches the game exactly*, so nothing learned in it is wrong, at the price of a movement lab that is unusable for half its cycle. (ii) *The harness is deliberately kinder and says so*, which is honest but means every night impression formed there must be discounted by hand. (iii) *Per-world night sight becomes a general tool* any world may set — the cheapest, and (per §8.3's logic applied to worlds rather than to cues) the one most likely to end with no two worlds agreeing about what the dark costs. **The director does not pick**, and notes that (iii) arrives by inaction: the seam already exists and each use individually looks reasonable. *Ripeness trigger: the first session in which Talon plays this world's night and camp's night back to back, or the first non-harness world that asks for its own range.* |

| How much of a chance a drowning owes the player | 7.1, 8.9, 3.4 | **No** | Added 2026-08-29 by the WATER-3 pass. Talon set the window at "about three seconds" and that is not the fork. The fork is what the window *buys*: derived from the shipped tuning it is about **4.1 m of swimming**, against about **4.05 m** from the innermost stepping stone to water you can stand up in — so the recovery exists and has under a second of slack, before any reaction time. §7.1's "avoidable but barely" is exactly that shape, which is why this is a fork and not a defect. **Three sides, and they belong to three different owners.** (i) *The window* (`WaterGeometry.DrownAfterSec`, Talon's) — more seconds buy margin and cost the hesitation the murk was authored for. (ii) *The swim speed* (`SwimSpeedMul`, the water contract's) — faster swimming buys the same margin without touching the number Talon named, at the price of making swimming a way to travel. (iii) *The stone spacing* (`GreenHills.tscn`, and already flagged by MOVE-8 as breaching `StoneGapMax` against the post-ruling tap range) — the crossing is the only reason anyone is over deep water at all, and it is the side nobody has costed. **The director does not pick, and notes that the numbers above are derived rather than measured in engine.** *Ripeness trigger: the first session in which someone actually falls off one of the inner stones — the margin is small enough that one honest attempt discriminates it, and no amount of arithmetic will.* **UPDATED 2026-08-29 (STONE-2), and the update is mostly a retraction.** Every derived number in this row was wrong in the same direction. Measured in engine by driving a real `CharacterBody3D`: the window buys **4.40 m**, not 4.1 (this row used `MoveSpeed` 3.6; the shipped value is 3.8), and the innermost stone needed **6.147 m**, not 4.05 — so the row's "under a second of slack" was in fact a shortfall of 1.758 m and **no heading survived at all**. §7.1's *avoidable but barely* was never the shape; it was unavoidable, which is a defect and not a fork. **A fourth side existed and nobody had named it: (iv) the BED — how wide the deep band is and how the island's flank is graded.** STONE-2 took that side, because it is the only one owned by neither Talon nor the water contract: the wall was replaced by a beach and the deep annulus narrowed from 6.65 m to 5.19 m, which is wider than the best-case reach (so the bank still cannot be left) and under twice the worst-case reach (so no landing is out of range of both shores). **The fork is NOT closed by this** — sides (i), (ii) and (iii) are all still live and still their owners'. What changed is that the fork is no longer sitting on top of a defect, and its trigger is now reachable: someone can fall off an inner stone and live to have an opinion. |
| What the flashlight costs | 8.7, 3.4, 2.3 | **No** | Added 2026-08-29 by NIGHT-2. The flashlight ships free, infinite and instantaneous, and that is the correct answer for a playtest whose stated subject is movement and exploration — but it is the first light in this repo that does not run out (the stick is consumable, the torch burns down, the fire eats wood), so §8.7's permanent-safety failure and §3.4's no-degradation failure are both live the moment the night contains anything. **This is a worse case than the *blind hide* row's** and worth naming as such: the hide at least costs a held button and takes your senses; this costs one keystroke and takes nothing. **Four sides, and the first is a real answer rather than a null.** (i) *Nothing* — the dark is a navigation problem, the light solves it, and the pressure comes entirely from what is *in* the dark rather than from the dark itself; honest, cheap, and it makes canon fact 5's cold-freeze do all the work alone. (ii) *A battery* — the obvious one, and the trap: a battery **is a clock** (§2.2), a second one, running per player, and the ledger's standing rule is that this level rides the shipped cycle and authors no other; a personal clock also converts the tool into a resource-management chore in a game that already has wood. (iii) *§2.3's trade — the light costs you safety* — it makes you the most visible thing in the clearing, which is the *Attention as a resource the group can move* row's scoring read backwards and needs no new system at all, only a creature that scores visibility; this is the strongest side on doctrine and the only one blocked on something that does not exist. (iv) *It costs a slot* — carrying it means not carrying something else, which is free today (the two-slot loadout already ships) and prices the light in inventory rather than in time or danger. **The director does not pick, and flags that (ii) is what everybody will reach for first and is the one this file has an argument against.** *Ripeness trigger: the first session in which the night contains a thing that can act on the player and somebody plays it with the light on. Until then there is nothing for the cost to be a cost against, and any number picked now is picked against an empty room.* |
| Whether a death has to name its own cause to a watcher | 4.1, 4.2, 6.0 | **No** | Added 2026-08-30 by W7-4. Every death now differs by cause in what the body DOES — a drowning sinks, a fall topples — which is §4.1 condition 4 honoured cheaply. But the Bubble Test's water is murky by direction (BT-9) and a sunk body is invisible within a second, so in the one level where the distinction exists nobody can currently see it. **Three sides, not two:** (i) *a death is always legible to a watcher* — the cause must survive murk, distance and night, which costs a cue and risks §8.1; (ii) *a death is legible only to the person who died* — §5.4's asymmetry taken seriously, and the watcher's uncertainty about what happened to a teammate is itself the dread; (iii) *legible for some causes and not others* — a creature's kill announces itself, an environmental one does not. **Trigger: the first session in which two players are in the same water and one of them drowns.** Until then there is no watcher to be uncertain, and a cue would be tuned against nobody. |

Per the ripeness rule (`DESIGN-BIBLE.md`, 2026-07-20): an unripe fork is not surfaced as a
decision — its trigger is proposed instead. The director adds forks here; it does not resolve
them and does not present them to Talon before their trigger fires.

---

*File scope note: written 2026-07-25 against a repo whose shipped playtest build contains no
pressure mechanic, no threat entity in its loop, no ambient bed, and no directed beat. Nothing
in Part I has ever been enforced and Part II records zero spends. **Current bite: none.** First
real test: directing the tidal island's night phase, read against the first recorded
multiplayer session. Passing looks like two things — a dispatched agent building an unrelated
level producing something that feels like the same game, and the director catching a repeated
device before Talon notices it.*
