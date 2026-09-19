# BUBBLE-1 — where the hundred bubbles are

Produced by `tools/dev/bubble1_capture.ps1`. **`--capture-cam` on every shot**, so nothing here is
framed by a bot's follow camera — six of the seven subjects are sealed rooms the bot is not even
in, and a follow-camera shot would have been a picture of the hub
(`docs/testing/TESTER-HANDOFF.md`: *"`--capture-cam` is MANDATORY or you get a false pass"*).

All shots noon, `--cycle-freeze`, capture at t = 18 s, 1280 × 720.

| Shot | What it is evidence of | Camera (world, `x,y,z,tx,ty,tz`) |
|---|---|---|
| `roomB/` | `RedTv`'s room — **three bubbles where there were none** | `-22.5,-16.8,5.0,-17.5,-19.0,-0.5` |
| `roomC/` | `BlueTv`'s room, the big one — three bubbles where there were none | `10.8,-16.8,5.2,17.5,-19.0,-1.0` |
| `roomD/` | `TangleTv`'s room, the 3 m ceiling — three where there were none | `-5.2,-17.6,-11.8,0.5,-19.0,-17.5` |
| `roomF/` | the sunken television's Deep Room — three where there were none | `-12.8,-17.0,-11.8,-17.5,-19.0,-18.0` |
| `lab/` | the puffin lab's main room — **four of its ten**, where it had none | `81,-9.5,97,91,-13.0,90` |
| `labhall/` | the lab's long hallway, looking east at the tiny door | `120,-19.5,72.06,160,-20.4,72.06` |
| `cyan/` | **the density shot**: cyan's whole 140 × 100 m plate at 18 bubbles | `-60,40,30,0,1,95` |

## What each frame is for

**The four room shots and `lab/` answer Talon's actual complaint** — *"there are not any bubbles at
all in most of the TV areas and in the puffing lab scene ... which is a shame because it's a really
cool place."* Each is a picture of a room that had zero, holding its new ones, so the claim is a
frame rather than a count in a table.

**`cyan/` is the one that has to be wide.** Cyan went 33 → 18 and the two lane runs down x = −30
and x = +30 went from seven and five bubbles to three and two. The claim the reduction rests on is
that **the line still reads at the lower count** — that the run still points somewhere — and that
is a property of the whole plate, not of any bubble. A close-up would show three nice bubbles and
prove nothing about the arrangement they are part of.

**`labhall/` is deliberately a dim frame.** The lab's hallway is a 55 m unlit corridor whose only
light is at the tiny door at the far end, and the near bubble reads against exactly that light.
That is what the shot is: a bubble is visible in the hallway, at the distance the hallway is
actually seen from. A brightened capture would be a picture of a different hallway.

## Rooms not shown, and why

**RoomE has no bubbles and no shot.** `BubbleTestWorld.DestinationFor` overrides `HiddenTv` — the
only television that ever went to RoomE — to the puffin lab's `Arrival` whenever the lab is in the
build, which is every shipped build. Nothing reaches RoomE, so a bubble there would be one the
player can never collect. See the BUBBLE-1 report.

**The Den (the hub television's room) keeps the three bubbles it shipped with** and is unchanged by
this packet, so there is no before/after to show.
