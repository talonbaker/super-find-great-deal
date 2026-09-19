# Watis World — project instructions

Godot 4.7 (C#/mono, .NET 8), PC/Steam. This repository is the **MVP extraction** of the `Sail`
development repository (2026-09-02): it carries only what the current playable build reaches.
`MVP-SCOPE.md` says what is in and out and why; `DECISION-LOG.md` records how each call was
made. Sail stays as the historical record — read it, never build against it.

## Canon

**This repo is an MVP about movement, flow, feel and mechanics. It has no premise** (Talon,
2026-09-02). There is no setting, no player noun, no story and no threat here — do not infer one,
and do not carry one over from `Sail`. Read [`docs/CANON.md`](docs/CANON.md) for the full
statement and the durable §0 tenets; it is short now, on purpose.

The level does have **one mechanical objective — collect all the bubbles** (Talon, 2026-09-04;
said once on entry, restated in HOW TO PLAY). That is an objective, not a premise: nothing is at
stake, nothing happens when it is met, and no setting may be inferred from it. Both this line and
`docs/CANON.md` said "and no goal" until that date — the direction changed, not the rule.

**Never tell Talon that something "contradicts canon."** The premise churns faster than any
document tracks it, so when his direction and a document disagree, the document is what is wrong —
update it or say nothing. If a task genuinely cannot proceed without a premise decision, ask him
one plain question rather than guessing.

A retired player noun still lurking in an old document, an identifier or a test fixture (`camper`,
`child`, `-uffling`, `caveman`, `KidQuarters`, `IsInCampfireWarmth`…) is **residue, not evidence of
a setting**. Read it as "the player", keep working, and don't sweep it — the mechanical rename is
its own deliberate pass.

**A dead link into `Sail` is provenance, not rot.** Documents carried over from `Sail` cite paths
that were deliberately left there — `docs/superpowers/**`, the dated `docs/agents/` briefs and
`outbox/`/`archive/` reports, `tools/dev/**`, and the cut asset contracts (`BLENDER-EXPORT.md`,
`ENVIRONMENT-ASSET-CONTRACT.md`). Measured 2026-09-02: 78 such references across 38 documents.
They record where a decision came from; read them in `Sail` and do not "fix" them by inventing a
local file. A reference to a path that should exist *here* and does not is a different thing, and
worth reporting.

Reference documents in `docs/`:

| Doc | Governs |
|---|---|
| `BEHAVIOR-BIBLE.md` | autonomous entities — AI, movement, chase/attack/flee/patrol |
| `MECHANICS-BIBLE.md` | game-system logic — state machines, boundaries, races, idempotency |
| `INTERACTION-BIBLE.md` | anything the player directly acts on — buttons, doors, pickups |
| `LEVEL-BIBLE.md` | composition — zones, spawns, paths, pacing, extraction, ambient legibility |
| `THRILL-BIBLE.md` | affect — what the player should *feel*. Applied via `/direct` |
| `DESIGN-BIBLE.md` | tiebreakers for ambiguous design calls |
| `ART-BIBLE.md`, `PROPORTION-STYLE.md` | colour, shading, silhouette, materials, stylization dials |
| `UI-DESIGN-SYSTEM.md` | the interface substrate — tokens, recipes, states, the layer ladder. Nothing in the UI holds its own appearance |
| `ANIMATION-CONTRACT.md` | the avatar clip library and its rest-pose contract |
| `STATE-CASCADE-TABLE.md` | every system a **player**-state change must update |
| `ATMOSPHERIC-VFX-INTEGRATION.md` | the map for the `vfx-*` skills |

## Where things are

```
scenes/Boot.tscn            the main scene; Boot.cs routes by launch flags
scenes/ui/                  splash, main menu, host/join/settings, first-run panels, pause
scenes/game/Gameplay.tscn   the match; Gameplay.cs is the hub node
scenes/game/world/bubbletest/   the playable level (seven authored sections + the seam)
scripts/net/                transport, handshake, snapshots, AvatarMotor, MotorTuning, Steam, hosting
scripts/game/               avatar, carry, bubbles, TV portals, water, failure, run spine, achievements
scripts/ui/                 the design system and every screen
scripts/voice/, scripts/telemetry/, scripts/controls/
tests/unit/                 the Godot-free xUnit suite (dotnet test)
tests/Run-*.ps1             the Godot scene suites (Windows PowerShell)
deploy/                     export scripts and SteamPipe templates
```

`ARCHITECTURE.md` is the mental model; `README.md` has the run commands.

## Rules that bind

Hard invariants live in `.claude/rules/` (`godot-scenes`, `imports-and-encoding`, `test-suite`)
and bind whether or not anyone read them. In particular: stage by path, never `git add -A`
(engine runs regenerate `.import`/`.uid` sidecars); engine flags before `--`, game flags after;
run both halves of the suite before a commit and quote raw counts.

Single-writer surfaces carried from Sail: `OutdoorAtmosphere.cs` is the sole writer of
sky/sun/moon/ambient (`SunShadowEnabled` stays false — Talon, 2026-08-07, closed);
`NightAidDriver.cs` is the sole writer of the `bt_darkness` shader global; `AvatarMotor.Step` is
the only thing that writes a player's velocity.

## Agent workflow

The file-based agent system is described in [`docs/agents/README.md`](docs/agents/README.md).
Three modes, and only Talon moves between them: **"just talking"**, **exploring** (default), and
**"task it out"** (orchestration per `docs/agents/ORCHESTRATOR.md`). Dispatched agents read their
`docs/agents/roles/<role>/ROLE.md` first and open with its `ROLE / PACKET / TOKEN` line.

## The bible check — required before any gameplay feature is complete

No gameplay feature is complete until it has been checked against every applicable bible. Pick
by what the feature *is*: moves or acts on its own → **Behavior**; resolves its own state,
timers, win/loss → **Mechanics**; the player acts on it → **Interaction**; arranges things into a
playable space → **Level**; meant to make the player *feel* something → **Thrill** via `/direct`.

State these three lines when marking gameplay work complete:

```
Bibles applied:  <which, and why those>
Items checked:   <the specific numbered items that bore on this feature>
Result:          <pass, or what was fixed to make it pass>
```

If no bible applies, say so and say why. When a design call is genuinely ambiguous: Behavior /
Mechanics / Interaction first, then `DESIGN-BIBLE.md`, then surface the fork to Talon and wait.
Ambiguity about a *value* is not a fork — pick a sensible value, state it, move on.
