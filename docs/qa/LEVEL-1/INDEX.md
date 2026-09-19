# LEVEL-1 — headed captures, before and after

Eight frames, four subjects, two states. `tests/Run-EggCapture.ps1` produced every one of them,
with the **same four cameras and the same two clock phases on both sides**, so a pair differs only
by what is in the level.

| subject | section it belongs to | before | after |
|---|---|---|---|
| `moat` | `BluePrecision` | [before/moat/EggCap-22s.png](before/moat/EggCap-22s.png) | [after/moat/EggCap-22s.png](after/moat/EggCap-22s.png) |
| `sunken` | `GreenHills` | [before/sunken/EggCap-22s.png](before/sunken/EggCap-22s.png) | [after/sunken/EggCap-22s.png](after/sunken/EggCap-22s.png) |
| `cyan` | `CyanRun` | [before/cyan/EggCap-22s.png](before/cyan/EggCap-22s.png) | [after/cyan/EggCap-22s.png](after/cyan/EggCap-22s.png) |
| `night` | the creature, level-wide | [before/night/EggCap-22s.png](before/night/EggCap-22s.png) | [after/night/EggCap-22s.png](after/night/EggCap-22s.png) |

## How "before" was produced, and what it therefore is and is not

**Read this before reading a pair.** "Before" is **not** a build of `main`. It is this branch's
tree with the four EGG-2-edited section scenes — `BluePrecision.tscn`, `CyanRun.tscn`,
`GreenHills.tscn`, `TvRoom.tscn` — temporarily replaced by their `Sail 583ecc02` content, captured,
and then restored. Each swapped file was verified against `Sail`'s blob hash going in and against
EGG-2's `825e1087` blob hash coming out; both checks are in the report.

So a pair isolates **EGG-2's scene edits and nothing else**. The C# is the combined tip on both
sides, which has one visible consequence worth stating rather than letting a reader discover:

- **The rainbow `SecretBubble` appears in BOTH `sunken` frames.** It is added from code by
  `BubbleTestWorld`, not authored into `GreenHills.tscn` (its class doc gives the reason: a node
  that builds its own mesh inside a section file would turn `BubbleTestSelfTest.CheckBake` red).
  Reverting the scenes cannot remove it. Its presence in the "before" frame is an artefact of the
  method, not evidence that it predates EGG-2.
- The same caveat applies to the Watcher in the `night` pair.

Anything that IS authored into a section scene — the moat, the sunken plinth, Cyan's gallery — is
absent from "before" and present in "after", which is what these pairs are for.

## What each pair shows

**`moat` — `BluePrecision`, noon, from the south-west.** The clearest pair of the four. Before: the
blue tower stands on continuous flat checkered ground and a fall off the balance beam is free.
After: a dark water basin fills the tower's footprint, specular glints across it, the four
`Ground/Apron*` bodies left as a dry rim. This is Talon's addendum §7 — *"falling means landing in
water and drowning"* — as a picture. It is also the one geometry change in the whole EGG-2 diff
that **removed** an existing node rather than adding beside it (the section's single `Ground` plate,
8 deleted lines).

**`sunken` — `GreenHills`, noon, from the south bank at eye height.** Before: the lake is empty
under the murk. After: the sixth television's plinth reads as a dark silhouette two metres down.
This is the honest answer to "can you see it from the bank" — a frame, not an assurance — and the
answer is *barely*, which is the intent.

**`cyan` — `CyanRun`, noon, looking down the second ruler.** The subtlest pair; EGG-2's Cyan work is
dressing (`FinishedRecord`'s lanes, ticks and skid marks; `Gallery`'s benches), not a change to the
lane. Judge it as an arrangement, which is what it was directed as.

**`night` — midnight, frozen, framing the bot's neighbourhood.** **These two frames are NOT the
pass/fail evidence for the night gate and must not be read as it.** The capture script says so in
its own header: the creature stands 18–62 m from whoever it is watching and never inside their view
cone, so a shot aimed at the bot systematically misses it. The gate's actual evidence is
`tests/Run-WatcherNightGateTest.ps1`, which asserts on the server log in **both** directions
(midnight: `sightings=1 state=Watching`; noon: none) and which passes on this tip. These frames are
the look at the level at night, nothing more.

## What has no frame, and why

**`TvRoom` is the fourth EGG-2-edited section and has no capture.** Its edit is `RoomF`, "the Deep
Room" — the sixth television's destination — and `RoomF` sits inside the sealed room block twenty
metres under the map, reachable only by taking the sunken TV. `Run-EggCapture.ps1` has no shot
inside the rooms and the capture bot cannot walk there. Rather than fake one, the structural
before/after is in the report: `RoomF` is absent before and present after, carrying 22 descendants
and — the point of criterion 3 — **none** of the four Dens' furniture, which the same census finds
7, 7, 8 and 6 of in `RoomB`/`RoomC`/`RoomD`/`RoomE`.
