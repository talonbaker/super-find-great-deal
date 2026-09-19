---
type: playtest-notes
from: Talon
date: 2026-09-04
subject: levels — the LD-6 kit course, walked on foot
build: Sail-ld6 @ 79e5807b (= origin/master 583ecc02 content; LD-1, LD-2, LD-4, LD-6 merged)
not-in-build: LD-5 Jolt (parked, b48fcf05), EGG-1 (PR #377), EGG-2 (PR #378)
status: VERBATIM — captured live, not triaged. No packet has been cut. Do not act on these
        until Talon says go.
---

# Talon's notes — 2026-09-04 level session

Per `docs/playtest/INTAKE.md` step 1: exactly as he said them, before any analysis.

## Raw, as spoken

> I'm not sure just keep it separate for now. This play test was fine. It wasn't anything
> special. That's all I really have to say it just didn't feel like much. I also noticed a
> camera issue when the player is running up onto a slope and they get to the very top of the
> slope the camera seems to jolt forward and sort of it looks like it goes between the players
> legs and shows their ankles like really up close. This is bad. This needs to be fixed. Please
> put this as a note to be worked on.

## Numbered

**1. The kit course — fine, unremarkable.**
> "This play test was fine. It wasn't anything special. That's all I really have to say it just
> didn't feel like much."

No specific defect named. Recorded as a whole-level verdict, not a bug.

**2. Camera jolt at a slope crest — FLAGGED BY TALON AS "to be worked on".**
> "when the player is running up onto a slope and they get to the very top of the slope the
> camera seems to jolt forward and sort of it looks like it goes between the players legs and
> shows their ankles like really up close. This is bad. This needs to be fixed."

His only explicit work request from this sitting. Note that this is a **camera/motor** symptom
observed in a level scene, not a level-geometry symptom — it is very likely reproducible outside
the kit course. That is an observation for triage, not a diagnosis, and no triage has been run.

## Standing answer given this session

Asked whether LD-1's metrics gym, LD-2's cadence readout and LD-4's wind speedometer (all of
which live in the movement playground, not in this scene) should follow as a third sitting:

> "I'm not sure just keep it separate for now."

So: **separate, not now.** Not scheduled, not dropped.

---

## Added after the sitting — the combined-build request

**3. The two playtests should have been one. FLAGGED BY TALON AS a note.**
> "Ideally, I would be testing the level design with the updated movement. So one would go with
> the other. I would like this added as a note." … "Please simply include the bike and bike
> movement in the level which is being tested. Because I need to roll down slpoes and things
> like this, right?"

The standing arrangement — bike in the movement playground, level in a separate KitCourse scene
— means the level is only ever judged on foot and the bike is only ever judged on the playground's
own courses. His two verdicts today were each produced under that split. **A level read taken
without the bike is not the read he wants**, and note 2's slope-crest camera jolt and the bike
notes' slope stutter are both slope symptoms found in two different scenes, which is itself an
argument for one scene.

Not acted on. No branch merged, no scene changed.
