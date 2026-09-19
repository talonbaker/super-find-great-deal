---
type: playtest-notes
date: 2026-08-30
player: Talon
build: C:/repos/Sail-playtest @ 35669f86 (code-identical to origin/master cdb10caa — every commit between is docs-only, verified)
world: bubbletest (default), hosted via Steam
session: drowned once and respawned at the spawn point, popped bubbles, flashed four TVs, dedicated server shut down gracefully, exit code 0
status: verbatim — NOT triaged
---

# Talon's notes — first play of the WAVE-5 build, 2026-08-30

**This file is the verbatim record and is never edited to match a later understanding.**
Triage, causes and packets live elsewhere and cite these numbers. Per `docs/playtest/INTAKE.md`:
verbatim first, then causes, one packet per cause, re-gate on the combined tip, report in his
numbering. The numbering below is the orchestrator's, added only so packets can cite an item;
the words are his, unaltered and in his order.

---

1. Please start the player slightly farther back, this will give more view of the level overall.

2. Please set default to full screen on start. If needed, please put a setting in the settings
   which can toggle / enable windowed mode.

3. On the bubble reset lever, when this pops up, please include in the text "press E to reset" and
   then when the warning is shown again, again include "press E to reset" to make it clear to the
   users what to do.

4. There is an issue where the cubes which are scattered throughout the level, when picked up, are
   not actually being "held" the hands of the player character don't actually move to indicate
   anything is being held.

5. The night says "head to the campfire" this needs to remove the "campfire" reference, it's not a
   campfire anymore.

6. When the player "drowns", the body floats and spirals upward, to the surface, into the sky, it
   looks like a tornado took them up. Please address this.

7. The golden cubes, in the day, are overblown like too bright.

8. Please keep one of the "TV sets" on the gray level, place. Please put the rest of the TVs at the
   top, in the hard to reach places of the world around. Please make these the reward at the end.

9. Please include one of these scenes as the "Lab" scene with the creepy lab from before. I would
   like this to be a "throwback" to the first game, with the "deformed" puffling from before.
   Please simply include this as something so the player can feel rewarded for exploring.

10. Please make a massive "jumble" of blocks, make one of them extremely tall, there are two please
    make one of them super tall and jumbly. This will be fun for the player to jump and test.

11. Please address the player character's feet. They don't move along with the body, they stay flat
    to the ground.

12. Please include four "Achievements" into the game:
    1) "Duck walk" where the player "duck walks" for the first time.
    2) "Tough guy" where the player holds right mouse click for three seconds at least the first
       time.
    3) "Tough guy duck walk" where the player makes a "tough guy" pose while in the "duck walk"
       which is like a combination of the two.
    4) "Bubblholic" where the player collects all the bubbles. I know this is a "shared"
       multiplayer game, but this will be fun nonetheless.

13. Please make the player character "ombre" like the other graybox was, wehre the feet are darker
    and do up toward the head which becomes lighter. This was a really cool look.

---

## Note 14 — added by Talon in the same message, after the thirteen

> "drowned once and respawned at the cave," Remember theer is no "cave" anymore. Please address
> this if only for clarity moving forward.

He was correcting the orchestrator's own summary wording, which had taken the word from
`RespawnService.cs:315`'s log line — so the stale noun is in the code, not only in the prose.
**This is a rename, and canon's protocol for a rename is one deliberate pass, never a sweep**
(`docs/CANON.md`, the same protocol that governs "quarters"/"camp tokens"). `cave` appears across
`Gameplay.cs`, `RespawnService.cs`, the `playtest1` world files and an asset path; almost all of
it is identifiers and logs rather than anything a player reads.

## Note 15 — added by Talon in a later message the same day

> "Also, I would like you to put a notice in the very beginning after the user starts the game for
> the first time and presses the start button. I would like this notice to say that anonymous usage
> is enabled by default to help the development process. On this notification, I would like there
> to explanation that explains anonymous usage can be disabled in the settings. And settings
> allows, allows them to disable usage report. And then a button to say don't show this message
> again with an X to click out. This is just like the pause menu or the how to play screen where
> they have a button but also an X but only this difference is that I want anonymous usage to be
> turned on by default and only the player can turn it off.
> I would like this to be something that the player themselves has to go into settings and disable.
> Once they disable this setting, please do not reenable it on them restarting the game. That would
> be very rude if they disabled the usage reports and then it was enabled again, and they did not
> realize it."

Routed into **W7-2** rather than its own packet: it needs the same settings screen and the same
persistence mechanism as note 2's windowed toggle, and two agents editing one settings UI is how a
merge conflict becomes a design conflict.

## Note 16 — added by Talon in a later message the same day

> "One of the things I forgot to mention was that when the player grabs a golden cube object, that
> golden cube object will grow in size. It's like it is one size when the player has it on the floor
> and then once the player picks it up then the cube is grown larger, and if the player puts it back
> down again, the cube stays grown larger, which is very strange please fix this."

**The persistence after the drop is the diagnostic half.** A cube that looked wrong only while held
would be a presentation problem; one that stays grown after being put down means the scale was
*written* somewhere durable, not applied for the duration of the carry. The most common cause of
that shape in Godot is a reparent under a node whose own scale is not 1 — the child keeps its local
scale, inherits the parent's, and whatever restores it on drop restores the compounded value.

Routed into **W7-3**, which is already diagnosing note 4 (the hands not moving when carrying) in the
same attach path. Two agents in one code path is how a merge conflict becomes a design conflict.
**The same seam rule applies:** if the cause is the carry system's rather than the avatar's, W7-3
reports it and the orchestrator routes the fix — it does not reach across.

## Note 17 — added by Talon in a later message the same day

> "There is a studio mark at the beginning when the game first opens up. I like this. It looks very
> nice. This naturally fades away into the start screen. But this just has a start button settings
> and quit, which makes sense but right when you hit start you go to what looks like just the same
> menu with an additional two options which is really stupid so what I would like is for you to take
> away the settings and quit and replace this with "press start" The studio mark is different than
> the splash screen and the splash screen should be different than the main menu. This is what I
> would like to see."

**Three screens, three identities.** The studio mark he likes and it does not change. The splash
becomes a single affordance — *press start* — and nothing else. The main menu is the only screen
carrying options.

**This note redefines the trigger in note 15.** That note places the first-run usage notice "after
the user starts the game for the first time and presses the start button", and note 17 changes what
pressing start *means*. Under the new flow, the notice belongs at the **first arrival on the main
menu**, i.e. immediately after the splash's press-start on a fresh profile. Both notes are W7-2's
for exactly this reason: they are one flow, and split across two agents they would contradict each
other.

## Machine-side observations from the same session — NOT Talon's notes

Recorded here because they are from this run and would otherwise be lost with the log. They are
the orchestrator's, not his, and carry no authority over the thirteen above.

- `[fire] no verb registered for active slot (kind=empty)` — six times. Pressing fire with an
  empty hand logs a line and does nothing else. Whether that should be silent or should give the
  player feedback is a design call nobody has made.
- `WARNING: Realtime Skies can only use a radiance size of 256.` from
  `scripts/ui/campfire/MenuBackdrop.cs:305` — the menu sky sets a radiance size Godot then
  overrides. Harmless; a value being set that can never take effect.
- A second backtrace from `scripts/game/bubble/BubbleCounterDisplay.cs:173` via
  `BubbleCounter.PopBroadcast` — so it fires on a bubble pop. **The captured tail is truncated
  above the header line, so the warning/error text itself is gone and its severity is unknown.**
  Not characterised, deliberately: it is reproducible on demand and guessing from the stack alone
  would put an invented cause into the record.
