# Playtest-note intake — how a note from Talon becomes a landed fix

**Why this file exists.** Talon's notes arrive as one lengthy batch, not as a trickle, and the
temptation each time is to read the batch, form an opinion, and start work on the interesting
one. That is how notes get merged, softened, or silently dropped. This is the order. It has been
run once end-to-end — his five 2026-08-29 notes, all five closed — and it worked, so it is
written down rather than re-derived.

The order is: **verbatim → triage → one packet per note → re-gate → report.** Never skip a step
to get to the fix faster.

---

## 1. Verbatim, first, before any opinion

Write the notes into `docs/playtest/<date>-talon-notes-<subject>.md` **exactly as he wrote
them**, before analysing anything. Not summarised, not grouped, not reworded into ticket
language. His phrasing carries information that a paraphrase throws away — the last batch turned
on the difference between "the lever doesn't work" and "the lever works twice."

Number them. The numbers become packet names and they are how you prove at the end that none
were dropped.

## 2. Triage — separate the note from its cause

**A note is a symptom report, not a diagnosis.** Nine notes have collapsed to four causes
before, and five notes have hidden three unrelated defects before. Do both directions:

- **Do several notes share one cause?** Then it is one packet, and it closes several notes.
- **Does one note hide several causes?** Then it is several packets, and the note is not closed
  until all of them are.

Read the code before you believe the mapping. The last batch produced three findings that no
amount of reasoning would have reached: `ServerKill` had **zero callers**, the lake was a
**half-plane**, and the lever gate was **only a debounce**. Each looked like a tuning note and
was a defect.

## 3. One packet per cause, dispatched to a role

Each packet gets: an id, a role, a fresh worktree off the current trunk tip, a base SHA, the
verbatim note it answers, and acceptance criteria that can fail. Cite `.claude/rules/` files
**by path** — never paste a rule's contents into a packet, or the packet becomes a second copy
that goes stale.

Two rules from experience:

- **Carve the blocker out of the big packet.** A packet holding a playtest-stopping question as
  one bullet inside a broad sweep returns neither. Split it and the blocker answers in half an
  hour.
- **A long-running agent with zero commits is exposure, not progress.** Nudge mid-packet to
  commit — without rushing the packet or trimming a second measurement run.

## 4. Re-gate on the **combined** tip, never on the branches

Every packet green on its own branch proves nothing about them together. Merge, then run **both**
commands on the merged tip:

```
powershell -File tests/Run-AllTests.ps1
dotnet test tests/unit/SailNet.Tests.csproj
```

Discriminate every red **standalone, twice**, on an idle machine
(`powershell -File tests\Run-<Name>Test.ps1 -SkipBuild`). **A defect does not move.** One red in
one run proves nothing; the same red twice is a defect; a red that alternates is load.

## 5. Report back note-by-note, in his numbering

Answer each numbered note with what changed and the evidence, including the ones where the
answer was "this was already correct" or "this is a design call, and it is yours." **Say which
notes you did not close and why.** A batch report that only lists wins is unreadable as
evidence.

## 6. Call it, before you touch it (LD-3, A4-R2)

Before a player touches a climbable structure for the first time, they say aloud whether they can
reach the top and by which side, before finding out. Whoever is scribing writes down hit or miss
per structure, not just the tally — the miss is the data. A structure most players call wrong is
a legibility defect in the kit (silhouette, lighting, an ambiguous face), not a skill gap in the
player, and it goes into the notes the same as any other note, numbered, in his words if he says
one. Do not run this retroactively from memory after the session; it only works called live.

---

## The trap that has already cost a session once

**Check which tree he actually played.** `C:/repos/Sail-playtest` has sat on a stale commit
before, which meant a set of fixes he was reporting on had never been in the build he launched.
Before believing a note that contradicts a known fix, confirm the SHA of the tree he ran. Before
handing him a build, confirm you moved that tree and imported it.

## What not to start while notes are pending

Do not open a **look** or **feel** packet that the notes might redirect. Correcting a verified
false claim in a document is safe; retuning a value or reshooting a capture is not. Screenshot
capture in particular stays parked until the darker pass is signed off — shooting a look he has
not approved is how you shoot it twice.
