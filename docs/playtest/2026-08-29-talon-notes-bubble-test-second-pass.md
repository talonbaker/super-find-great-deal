---
type: playtest-notes
from: Talon
date: 2026-08-29
build: C:/repos/Sail-playtest at ac842ee1 (gameplay-identical to trunk 36be9d56; the one
       commit between them is docs-only)
launch: plain launch, no flags — splash → menu, default world `bubbletest`
session: second pass of the day; supersedes nothing, adds to
         2026-08-29-talon-notes-bubble-test.md
---

# Talon's notes — 2026-08-29, bubble test, second pass

**Verbatim, before any triage.** His wording is preserved exactly, including phrasing that a
paraphrase would flatten. Numbering is mine, added only so packets can cite a note; the text
inside each is his. The last batch turned on the difference between "the lever doesn't work"
and "the lever works twice", which is why this file exists before any opinion does.

---

## 1 — the accent colour

> The colors for the UI on the main menu is perfect. Please keep them exactly as they are.
> However, after the initial "start game" screen section, it's back to the orange accent color.
> I would prefer this main UI color, the cooler color, to be places around and replace all the
> orange in the game UIs.

## 2 — HOW TO PLAY, and UI scale

> "HOW TO PLAY" section needs better UI. Needs icons for right mouse button, left mouse
> buttons, shift, etc. these should have keys, icons which go with the keys. Please make this
> update. Also, please make the overall UI bigger so the text is bigger and the icons go along
> with this. Please make the Please make the UI text overall slightly bigger, too.

## 3 — content to add to HOW TO PLAY

> Please include the following in the "HOW TO PLAY" section:

**The list that was meant to follow this line is not in the message.** It is not inferred and
not guessed at — the question is with Talon. Everything else in note 2 proceeds without it;
this is a content insert into a screen that is being rebuilt anyway.

## 4 — the loss condition

> There is a big problem with the current build of the game and that's there's still a "loss
> condition" which says "THE CACHE RAN DRY
>
> Night 1: the cache held 0 of the 3 winter needed.
>
> Winter wins this one. Everyone walks away - the cache doesn't.
>
> [ PLAY AGAIN ]
>
> [ LEAVE TO MENU ]"
>
> This is a problem for two reasons because first there is no "cache" there is no "winter" and
> there's no "goal" for this playtest, it's about movement and exploration. Second, there is no
> way for the user to "select" these reset / restart options. Please remove it entirely but
> this is a big problem overall.

## 5 — every other menu matches the main menu

> I love the new UI main menu it's perfect. Please make the rest of the menus match this main
> menu UI. Please update the UI so also the join and how to play and host and all these things
> look like this with the same accents and buttons and things.

## 6 — the reset-lever warning

> Along with the main menu UI changes, please include a non-diegetic "pop up" something like
> this, to warn the player that if they toggle the switch in the main level it will reset their
> bubbles. Please make sure they know this because the warning is good but it's too small for
> them to read and usually the player character is blocking the text anyway.

## 7 — box kid vs greybox

> I really like how the "graybox" model looks and feels, I don't like how "box kid" model looks
> and feels. Please fix this so box kid model looks exactly like how graybox is as the only
> different is the box kid is a physical glb and the graybox is generated. Please make this
> change using the exact dimentions of the graybox to make box kid better.

## 8 — spacing

> Please make each section overall slightly closer together. everything is a big too spread
> apart.

## 9 — more TVs, more destinations

> Please add a few more TVs in the level and give the player different areas to teleport to
> when going through each of the TVs. Please make each TV area take them to a slightly
> different room, similar to the room already made only slightly different.

## 10 — night visibility and the flashlight

> Also, when night falls, it's extremely difficult to see anything. there is text that reads
> "night has fallen" can you change this to say "Night has fallen. Press F to toggle
> flashlight."

## 11 — the flashlight glow

> Give the player a simple "glow" when they "toggle" this flashlight.

## 12 — networked carry, and a proper test for it (added mid-dispatch)

> There's one thing that I forgot that is very important to add to this current play test and
> that is the ability for users to pick up objects and move them around in a shared network kind
> of space. This has already been proven to work so it shouldn't be an issue however there
> should be a a kind of test nonetheless to make sure that this is completely working completely
> flawless. I would like there is a very initial play test with a bunch of golden cubes. This
> would be nice, please put these in and around the map allow the players to pick them up and
> throw them with E and Q this is already created. You don't need to do this again.
>
> This is confirming functionality that I hope it still functional. It just needs to be included
> as a proper test.

**Orchestrator's note, and it is not a comfortable one.** He expects this to pass. The corpus
disagrees about one part of it: `Carry: regrab-while-loose` was measured **3 fails in 4**
standalone samples earlier today (see `.claude/rules/test-suite.md`), and it is the suite whose
verdict a prior handoff called "deterministic" off an incomplete sample. So the one thing he
assumes is safe is the one thing our own measurements are least sure about. That is recorded
here rather than smoothed over, and it is why note 12 became its own packet instead of a bullet
inside the level work.

---

## Closing instruction

> Please do this all now dispatch these updates. Task it out.
