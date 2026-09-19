# Super Find Great Deal — project instructions

Godot 4.7 (C#/mono, .NET 8), Windows, PC/Steam. Seeded 2026-09-19 from `Watis_Game@93bef3d`
(BASE-1): the multiplayer foundation kept, the old game removed. History for anything that came
over lives in `Watis_Game` — `git log -L` archaeology is done there, and a link into it is
provenance, not rot.

## The game

Two players, three rooms, one round.

1. **Holding room.** Both players. The hider picks one object off a rack of near-misses — same
   colour, same size class, a shape apart. Press Start.
2. **Hiding (30 s).** The hider is in the search room with the object, among aisles of identical
   red cubes. The seeker is shut in the holding room and cannot see or hear them.
3. **Seeking.** The hider is moved to the task room and stacks blocks for score; the seeker hunts
   the search room for the one object that does not belong and drops it in a bin.
4. **The payoff.** The moment the object lands in the bin, the door to the task room bursts open
   and the seeker walks in. The hider's tower is mid-stack and gets knocked over. Score freezes at
   that tick.
5. Tally, roles swap, again.

**The game is anticipation with an unknowable instant, paid off physically.** Both players know
the door is the only way this ends; neither knows when. The seeker's progress is invisible except
through what they say on the intercom — and they can lie. Voice is always on: same room is
proximity, cross-room is a filtered PA/intercom. Bluffing is the design, not a side effect.

First person. Hands-on carry (R.E.P.O.-style: the object is held by a spring, not parented to a
socket). Nothing is a cutscene and nobody ever loses input.

The full plan, including what the core opens up later, is
[`docs/design/2026-09-19-supermarket-mvp.md`](docs/design/2026-09-19-supermarket-mvp.md).

## Where things are

```
scenes/Boot.tscn                     the main scene; Boot.cs routes by launch flags
scenes/ui/                           splash, main menu, host/join/settings, first-run panels, pause
scenes/game/Gameplay.tscn            the match; Gameplay.cs is the hub node
scenes/game/world/supermarket/       the level: a seam file + three room scenes
scripts/net/                         transport, handshake, snapshots, AvatarMotor, Steam, hosting
scripts/game/props/, sandbox/feel/   networked carry authority + the hand-held carry feel
scripts/game/round/                  the engine-free round loop (imported, not yet wired)
scripts/game/world/                  SupermarketWorld, RoomTeleport, the cycle/audio plumbing
scripts/ui/                          the design system and every screen
scripts/voice/, telemetry/, controls/
tests/unit/                          the Godot-free xUnit suite (dotnet test)
tests/Run-*.ps1                      the Godot scene suites (Windows PowerShell)
deploy/                              export scripts and SteamPipe templates
```

`docs/PRUNE-BACKLOG.md` lists what the fork left behind on purpose and why.

## The two suite commands

**"The full suite" is TWO commands. Neither one alone is the full suite.**

```
powershell -File tests/Run-AllTests.ps1                 # the Godot scene suites (Windows)
dotnet test tests/unit/SailNet.Tests.csproj             # the Godot-free xUnit suite
```

Run both before every commit. Report RAW COUNTS from each run's own `=== summary ===` block,
never a verdict and never a number remembered from last time. Run the scene suite in the
FOREGROUND, redirected to a file (`> suite.txt 2>&1`), and read the file — a pipe reports the last
element's exit code and hides the runner's.

## Rules that bind

Hard invariants live in `.claude/rules/` and bind whether or not anyone read them:

- [`.claude/rules/test-suite.md`](.claude/rules/test-suite.md) — the two commands, foreground,
  raw counts, one suite per machine, the measured load-flaky list and how to discriminate a flake
  from a regression.
- [`.claude/rules/godot-scenes.md`](.claude/rules/godot-scenes.md) — **every part of a level is
  authored in its scene file, never built in code**; `Transform3D`'s twelve floats are basis ROWS;
  groups go in the `[node]` header.
- [`.claude/rules/imports-and-encoding.md`](.claude/rules/imports-and-encoding.md) — `.import`
  churn is line-endings, not content; never `git checkout -- '*.import'`; never round-trip a file
  through PowerShell `Get-Content`/`Set-Content` (5.1 mangles UTF-8).

In particular, three that cost a session each:

- **Stage by path. Never `git add -A`** — an engine run regenerates ~141 `.import`/`.uid`
  sidecars with zero content diff.
- **Godot CLI: engine flags before `--`, game flags after.** Wrong side is swallowed and looks
  exactly like a hang.
- **`user://` resolves by project NAME**, so every checkout of this project on a machine shares
  one profile (`%APPDATA%/Godot/app_userdata/Super Find Great Deal/`). Gate any persisted
  per-player fact on `IIntentSource.IsHumanInput` — a suite bot once spent a real achievement
  into the player's live profile in the repo this came from. **Never touch Watis World's
  profile**; the rename at the fork is what keeps them apart.

## The bible check — required before any gameplay feature is complete

No gameplay feature is complete until it has been checked against every applicable bible in
`docs/`. Pick by what the feature *is*: the player acts on it → **INTERACTION**; it resolves its
own state, timers or scoring → **MECHANICS**; it arranges things into a playable space →
**LEVEL**.

State these three lines when marking gameplay work complete:

```
Bibles applied:  <which, and why those>
Items checked:   <the specific numbered items that bore on this feature>
Result:          <pass, or what was fixed to make it pass>
```

If none applies, say so and say why. `INTERACTION-BIBLE.md` is the one with scar tissue in it —
the one in-world control this foundation ever shipped failed three playtests in a row, and §2/§3/
§5/§7 are what those failures became. Read them before building a button.

## Asking

Ask Talon in plain conversational text, one question at a time, and wait for a plain-text answer.
Never a multiple-choice popup, an option list or a numbered batch. Define any jargon the question
uses.
