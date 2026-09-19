---
type: report
from: programming
to: orchestrator
packet: COPY-1
status: ready
branch: feat/2026-09-04-copy-1-playtest-copy
base: playtest/2026-09-04-combined
date: 2026-09-04
---

# COPY-1 — Talon's playtest copy, and the windowed toggle

## The one sentence a dispatcher can act on

**All three copy changes ship verbatim, and Talon's toggle report was a real bug that was never
only about the toggle** — Godot's `BaseButton::set_pressed` is a no-op while `toggle_mode` is
false, so all three code-built pills in the settings panel opened UNCHECKED regardless of the
setting behind them. Reproduced headed on the unfixed tree: `FULLSCREEN` read off over a fullscreen
game, and **`ANONYMOUS USAGE REPORT` read off over a session that was in fact reporting** — the
exact failure that row's own comment was written to prevent, on the same screen Talon's new
foreword sends every playtester to.

## Current state

On `feat/2026-09-04-copy-1-playtest-copy`, branched from `playtest/2026-09-04-combined` @ `36724b4`:

| File | Change |
|---|---|
| `scenes/ui/PlaytestForewordPanel.tscn` | Title → `Watis this game?`; `Intro`+`Goals`+`Footer` collapsed to one `Body` Label carrying Talon's prose verbatim; panel 480 → 720 px wide |
| `scripts/ui/HowToPlayContent.cs` | Five agent lines → Talon's three, verbatim; doc comment records the rule override and its date |
| `scripts/ui/SettingsPanel.cs` | `FULLSCREEN` → `WINDOWED` with the binding inverted; `ToggleMode = true` before `ButtonPressed` on all three pills |
| `tests/unit/ForewordCopyTests.cs` | New. 19 pins on his words, the inversion, and the pill ordering |
| `tests/unit/GoalCopyTests.cs` | Two assertions rewritten — they were written against agent copy and became assertions against his |
| `DECISION-LOG.md` | §6 appended |
| `docs/qa/COPY-1/` | Seven headed frames |

`scripts/ui/PlaytestForewordPanel.cs` was read and **not** changed — the scrim, layer, close, ESC,
`DontShowRow` and stagger-in it wires all still resolve against the rewritten scene. Nothing under
`scripts/ui/hud/**`, `scripts/voice/**`, `scripts/game/audio/**`, `project.godot`, any shader,
`Branding.cs`, `docs/CANON.md`, `CLAUDE.md` or `DeadNameAuditTests.cs` was touched.

## Corrections to the packet

**1. The TIDE diagnosis in the packet is wrong, in both halves.**

The packet says `DeadNameAuditTests` missed `TIDE` "because it sweeps source, not `.tscn` scene
text", and that extending it to scene files would be the fix. **It already sweeps scene files** —
`DeadNameAuditTests.UiFacingSources` enumerates every `.cs` under `scripts/ui/` *and* every `.tscn`
under `scenes/ui/` (`tests/unit/DeadNameAuditTests.cs:77-80`), and a fourth test,
`UiSources_TheSweepActuallySweptSomething`, exists specifically to assert that a `.tscn` was in the
swept set. `PlaytestForewordPanel.tscn` was inside that sweep the whole time.

The real reason is different and more useful: **the audit hunts exactly one name.** `DeadName` is
`"SLEEP" + "AWAY"`, assembled at runtime, and the file's own doc scopes itself to CORE-PROG-B1's
legal criterion — the 1983 film collision described in the title-clearance brief. `TIDE` is a
*different* retired name and was never in scope. Relatedly, the packet says `Branding.cs` "documents
[TIDE] as retired for legal reasons": `Branding.cs` never names TIDE at all, and the legal
collision it describes is the Sleepaway name.

**Is it cheap to close?** Yes, but not the way the packet framed it. It is one entry in the audit's
name list — no new scan surface needed. Measured on this tree: `grep -rin "tide" scripts/ui/
scenes/ui/` returns **0** hits after this change, so adding it goes green immediately with no
false positives today. The residual cost is that "tide" is an ordinary English word, so a
case-insensitive substring match would fire on legitimate future copy; a word-boundary match, or
requiring the all-caps form, is the version worth shipping. **Not done here, per the packet.**

**2. Two `GoalCopyTests` assertions had to change, and the packet did not mention them.**
`HowToPlay_OpensWithTheSameGoalTheToastStates` pinned `HowToPlayContent.Lines[0]` to start with
`PhaseToastText.BubbleGoalToast`; `TheGoalDoesNotInventAFailState` required the phrase "no way to
lose" somewhere in the list. Talon's three lines satisfy neither. Per `CLAUDE.md` — when his
direction and a document disagree, the document is wrong — both were **rewritten, not deleted**,
each carrying a dated paragraph saying what retired and why. The objective itself is untouched and
still announced on entry by the toast; the first two tests in that file still hold it. What is gone
is only the coupling between the toast and the HOW TO PLAY screen, which is his to break.

**3. The death-counter fork is withdrawn, resolved by Talon.** The packet asked for it as a ripe
fork with two costed options. Talon ruled directly, 2026-09-04: *"About the 'death counter' I'm
making a joke about the HUD bubble counter. Just a joke you know."* The referent is the shipping
`Hud.HudBubbleCount` (`GameHud` builds it at the head of the top-centre column behind the profile's
`BubbleCount` flag) — the gag is that the visible bubble tally is secretly a body count. Nothing was
built, nothing in `scripts/ui/hud/**` was touched, and the line ships unchanged. Recorded in
`HowToPlayContent`'s doc and pinned by `GoodToKnow_KeepsTheBubbleDeathCounterJoke`, which also
asserts `HudBubbleCount.cs` still exists so the joke cannot outlive its referent silently.

**4. The `--windowed` capture route.** The packet did not say how to reach the settings row without
taking the display. Boot's `--windowed` override does not change `DisplaySettings.Fullscreen` — it
only skips `ApplyWindowMode()` — so the pill's binding is fully exercised in a 1280x720 window.
That is how the *bug* was reproduced without stealing the screen; the fullscreen→windowed *round
trip* still needed one real fullscreen launch, which was taken and released in under a minute.

Nothing else in the packet needed correcting. The `'F'` claim is true as stated, `psydo`, `breath`,
the nine-dot ellipsis and `Night Lurker` all ship exactly as written.

## 1. The foreword panel

`Center/Panel/Header/Title` reads `Watis this game?`. The body is one `Body`-variation Label
carrying his prose, blank lines preserved, in the collapsed structure the packet asked for. Scrim,
header, X, ESC-to-close, `DontShowRow`, the stagger-in and `UiLayers.PauseChildPanel` are all
unchanged and asserted unchanged by `TheForeword_KeepsItsScrimCloseAndDontShowRow`.

**Evidence: `docs/qa/COPY-1/01-foreword-1280x720.png`.** Every paragraph legible, nothing clipped,
the panel inside its frame with ~65 px clear above the title and ~72 px below the ESC hint.

**How it was kept readable.** The new body is taller than the old — 19 rendered lines against the old body's 15,
and no headings to break them up. The only dial changed was **width: `custom_minimum_size` 480 →
720 px.** Measured across three headed passes:

| Panel width | Rendered body | Result |
|---|---|---|
| 480 (old) | — | the new body does not fit 720 p |
| 640 | 21 lines | fits, ~40 px margin, two one-word orphan lines (`TV.`, `settings menu!`) |
| **720 (shipped)** | **19 lines** | **fits, ~65 px margin, no orphans** |

No scroll was added, no type size changed, no spacing token touched — the letter is read in one
screen the way it was written. Width was a value fork, so it was picked and stated.

**The markdown asterisks ship as literal characters, and here is why that was not a choice.**
Making the body a `RichTextLabel` would take it out of the design system: `RichTextLabel` is not in
`UiThemeFactory.CoveredTypes` and has no styling anywhere in the theme, so it would render in
Godot's default font, size and colour. The only `RichTextLabel` in the repo is in
`scripts/dev/MotorTuningPanel.cs`, a dev instrument that hand-overrides its own font size — the
exact pattern `UiNoBespokeStylingTests` forbids in shipped UI. Adding the control type is an edit to
`scripts/ui/design/`, outside this packet. The packet's stated fallback was taken; the asterisks are
in the capture and pinned by test.

**The dead name that was live.** The old `Intro` opened *"TIDE is an early multiplayer playtest…"*,
legible in **`docs/qa/COPY-1/00b-before-fix-foreword-dead-name.png`** — the panel as a fresh
playtester met it on the unfixed tree. Talon's rewrite removes it. After this change `TIDE` appears in no
`.tscn` and in no player-visible string anywhere in the repo — the only code hit left is a comment
in `Boot.cs:255`. See **Corrections §1** for why the audit did not catch it.

## 2. GOOD TO KNOW

`NotesTitle` stays `GOOD TO KNOW`. The five previous entries are replaced by Talon's three, one
array entry per paragraph, in his order, with his spelling.

**Evidence: `docs/qa/COPY-1/02-how-to-play-good-to-know.png`** — all three paragraphs complete and
legible. The panel's card is taller than 720 p with the control table above the notes, so the frame
is taken after a small scroll. **That is pre-existing behaviour and needed no code:**
`HowToPlayPanel.Build` already puts the notes inside a `ScrollContainer` with `FollowFocus`
(`scripts/ui/HowToPlayPanel.cs:116-131`), and the five-line version scrolled the same way. Nothing
outside the owned file set was touched to make this frame.

**The doc comment was updated rather than left in violation.** It previously forbade copy naming a
mechanic the world does not have. It now records that rule as **suspended for copy Talon authored
himself, dated 2026-09-04**, states that it still governs anything an agent writes there on its own
initiative, and carries his death-counter quote and its referent. Line 2's drowning claim is still
true against shipped code (`RespawnCause.Drowned`, `WaterGeometry.DrownAfterSec` = 3.0 s).

## 3. The display toggle — and the bug under it

### The change

Label `WINDOWED`. Initial `ButtonPressed = !DisplaySettings.Fullscreen`. Handler
`SetFullscreen(!on)`. **The binding negates; the setting does not** — `DisplaySettings.Fullscreen`
and `SetFullscreen` keep their meaning for Boot's real-client branch, `settings.cfg` and the export.
The node names stay `FullscreenRow`/`FullscreenToggle` because they name the setting, which did not
change; renaming them would have been a second, silent edit. The comment at `SettingsPanel.cs:181-192` that explained
the old FULLSCREEN wording now explains the WINDOWED wording, names Talon and the date, and says
explicitly that this was his call rather than a rediscovered principle.
`TheStoredSetting_StillMeansFullscreen` fails if anyone ever "simplifies" the inversion by flipping
`DisplaySettings` itself, which would boot the game windowed for everyone.

### Talon's mismatch — REPRODUCED on the unfixed tree

**Yes, and with a second victim he did not report.**

Setup, stated so it can be re-run: the shared `user://` profile (`%APPDATA%/Godot/app_userdata/
Watis World/settings.cfg`) read `[display] fullscreen=true`, and `telemetry/` contained no
`usage_optout.marker` and `settings.cfg` no `usage_consent` key — so
`TelemetryStore.UsageReportingEnabled` was `true`. Base tree = the detached
`playtest/2026-09-04-combined` tip, launched headed at 1280x720.

**`docs/qa/COPY-1/00-before-fix-fullscreen-pill-unchecked.png`** shows, in one frame:

- `FULLSCREEN` — **unchecked**, while `DisplaySettings.Fullscreen` was `true`. Talon's report.
- `ANONYMOUS USAGE REPORT` — **unchecked**, while reporting was on. Not reported, and worse.

**Cause.** Godot's `BaseButton::set_pressed` returns immediately when `toggle_mode` is false.
`PillToggle` sets `ToggleMode = true` in `_Ready`, which does not run until the node enters the
tree — so in `new PillToggle { …, ButtonPressed = x }` the assignment lands on a button that is not
yet a toggle and is **silently dropped**. Every code-built pill on the panel opened unchecked
whatever its setting said. A `.tscn`-instantiated pill is immune, because a child's `_Ready` runs
before its parent's — which is why the foreword's own "don't show this again" pill never had it, and
why this went unnoticed.

All three code-built pills had it: the display row, `REDUCE HUD MOTION` (whose correct value
happened to be `false`, so it looked right) and `ANONYMOUS USAGE REPORT`. That last row's own
comment argued that showing an OFF pill over a reporting session would be *"a settings screen lying
about its own setting"*; the trap made it do exactly that to every player who had never touched it.
**Talon's new foreword sends every playtester to that row.**

**Fixed** by assigning `ToggleMode = true` before `ButtonPressed` in all three initializers, with
the mechanism written up once above `BuildDisplayRows`.
`EveryCodeBuiltPill_SetsToggleModeBeforeButtonPressed` asserts it structurally, so a fourth pill
added later cannot reintroduce it, and `ThePillSweep_WouldActuallyFail` plants a removal in the real
file and proves the sweep sees it.

### The round trip — measured as window rects, not claimed

| Step | Window rect | `settings.cfg` |
|---|---|---|
| Launch (real client, `fullscreen=true`) | `0,0 – 3440x1442` | `fullscreen=true` |
| Settings opened — `WINDOWED` **unchecked** | `0,0 – 3440x1442` | `fullscreen=true` |
| **Check `WINDOWED`** | **`1071,292` `1298x767`** | **`fullscreen=false`** |
| Relaunch — boots windowed, pill **checked** | `1071,292` `1298x767` | `fullscreen=false` |
| **Uncheck `WINDOWED`** | **`0,0 – 3440x1442`** | **`fullscreen=true`** |

The window genuinely changes mode, immediately, in both directions, and the state survives a
relaunch and reads back correctly. Frames:
`03-settings-windowed-unchecked-game-fullscreen.png`,
`04-settings-windowed-checked-game-windowed.png`,
`05-roundtrip-relaunch-windowed-still-checked.png`.

Note that `04` and `00` are the decisive A/B for the pill fix rather than `03`: with
`fullscreen=true` a *correct* `WINDOWED` pill and a *broken* one both read unchecked. The
discriminator is the `ANONYMOUS USAGE REPORT` row in the same frame — off in `00`, on in `04` — and
the relaunch in `05`, where a broken pill would read unchecked over a demonstrably windowed game.

**The shared profile was left exactly as found.** Backed up before the first launch and `diff`-ed
after the last: byte-identical, no opt-out marker created. The `session_active.marker` was cleared
before and after every launch. **No Godot process other than the ones these captures started was
inspected or signalled** — each was resolved by parent PID from the console wrapper this harness
launched, never by window title or process name.

## Bible check

```
Bibles applied:  Interaction — the display row is a thing the player directly acts on. Mechanics
                 was read and does not bite (no state machine, no race, no shared adjudication).
                 Behavior and Level do not apply: nothing here moves or occupies space. Thrill was
                 not applied — /direct gates affect-bearing final forms, and this packet ships
                 Talon's own words rather than authoring a feeling.
Items checked:   §2 feedback at the moment of the trigger — the window changes mode in the same
                 call that persists it, measured as a rect above, not on next launch.
                 §3 physical/visual correspondence — a row that says WINDOWED now puts the game in
                 a window; before this packet it said FULLSCREEN and drew a state unrelated to the
                 window's actual mode.
                 §6 reversibility — a freely repeatable toggle, persisted, proven in both
                 directions and across a relaunch.
                 §7 interrupt handling — untouched; the foreword and HOW TO PLAY still consume ESC
                 in _Input ahead of PauseOverlay's _UnhandledInput.
Result:          Pass, after the fix. §2 and §3 were both FAILING before it: the pill reported a
                 state it had never been given, so its drawn state and the world's state were
                 unrelated on every open.
```

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | `dotnet build WatisWorld.sln` | **0 errors**, 4 warnings — the same 4 as the base |
| 2 | Foreword capture, 1280x720, no overflow | `01-foreword-1280x720.png`; width 480 → 720, no scroll, no type change |
| 3 | HOW TO PLAY capture, three paragraphs | `02-how-to-play-good-to-know.png` |
| 4 | Settings before/after frames | `03` (fullscreen, unchecked) and `04` (windowed, checked), plus `00` and `05` |
| 5 | A test pins the copy, proved able to fail | `ForewordCopyTests`, 19 pins; six fired on planted edits |
| 6 | `dotnet test` raw counts | below |
| 7 | `Run-AllTests.ps1`, foreground, `OVERALL:` | below — 44/46, both reds discriminated against the base |
| 8 | This report | here |

### 6. xUnit — raw counts

Baseline measured first, on a throwaway detached worktree of this packet's own base
(`C:\repos\Watis-copy1-base`, `playtest/2026-09-04-combined` @ `36724b4`), not inherited from any
report:

```
base    Failed: 0, Passed: 2223, Skipped: 0, Total: 2223
COPY-1  Failed: 0, Passed: 2242, Skipped: 0, Total: 2242
```

**+19, all of them `ForewordCopyTests`.** `GoalCopyTests` keeps its count — two tests rewritten, none
added or removed.

### 5. Proving the pins can fail

Three edits planted in the real files, the suite run, then reverted:

| Planted | Fired |
|---|---|
| `psydo-proximity` → `pseudo-proximity` in the `.tscn` | `TheForeword_BodyIsTalonsWordsExactly`, `TheForeword_KeepsTheDetailAProofreaderWouldRemove(psydo)`, `TheSceneReader_WouldActuallyFail` |
| `breath it in` → `breathe it in` | `GoodToKnow_IsTalonsThreeLinesInHisOrder`, `GoodToKnow_KeepsHisSpellingOfBreathe` |
| `ButtonPressed = !DisplaySettings.Fullscreen` → un-negated | `TheDisplayRow_SaysWindowedAndInvertsTheBinding` |

`Failed: 6, Passed: 13` on the mutated tree; `Failed: 0, Passed: 19` on the real one.

**The pill sweep caught a bug in itself on its first run, against a correct file.** The comment
explaining the ordering names `ButtonPressed`, and `IndexOf` found it in the comment before the
assignment — so the test reported a correctly-ordered initializer as wrong. Fixed by stripping line
comments before indexing. An unplanned positive control, and the reason the sweep's all-clear is
worth something.

### 7. Scene suite

```
powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180
OVERALL: FAIL
```

Foreground, redirected outside `tests/logs/`, read from the run's own `=== summary ===` block with
the anchored `^  .+ (PASS|FAIL)$` pattern:

**46 suites, 44 PASS / 2 FAIL.**

| Red | Failing check, by name | Verdict |
|---|---|---|
| `Carry: regrab-while-loose` | `bot A never completed held -> loose -> held-again for prop 2 (reached phase 2) - regrab staging broken` | **Known load flake.** `.claude/rules/test-suite.md` lists this suite by name and says its reds are STAGING failures — read the failing check's name. This is that check, verbatim. The measured quantity (hold distance) never got a chance to be wrong. |
| `Netcode: net-sim` | `bot3 saw a remote render step of 3.303m (> 0.35m = visible jump)` | A smoothness assertion under simulated 80 ms / 5 % loss / 15 ms jitter. Not on the documented flaky list. |

**The machine was not idle.** SHADER-1's suite was running out of `C:/repos/Watis-shader1` in
parallel — 14 headless Godot processes (a server on udp/7814 plus six bots) were live on the box
while this marathon ran, confirmed by process listing. Both reds are latency/scheduling assertions,
which is exactly the family that goes red under load.

**Neither red can be caused by this packet.** The changed files are one `.tscn` label, one string
array, one settings row's label and binding, and two test files. Nothing here is reachable from
`scripts/net/**`, `PropManager`, `CarryController`, `AvatarMotor` or `SnapshotBuffer`; no suite in
either red instantiates `SettingsPanel`, `HowToPlayContent` or the foreword scene. The `--net-sim`
and regrab suites launch dedicated servers and headless bots and never open a menu.

**Standalone discrimination, on an idle machine** (process-listing checked before each side;
`-SkipBuild`; nothing else running):

| Suite | Tree | Runs | Result | Measured quantity |
|---|---|---|---|---|
| `Carry: regrab-while-loose` | COPY-1 | 3 | **3 PASS** | worst hold distance 1.07 / 1.07 / 1.08 m (A), 1.08 / 1.08 / 1.09 m (B) |
| `Netcode: net-sim` | COPY-1 | 3 | 2 PASS / 1 FAIL | bot3 `peak remote step` **3.302 m** on the red |
| `Netcode: net-sim` | **base @ `36724b4`** | 4 | 3 PASS / **1 FAIL** | bot3 `peak remote step` **6.409 m** on the red |

**`Carry: regrab-while-loose` is settled**: 3/3 green standalone, and the quantity the suite exists
to measure — hold distance — was never out of band. It failed its STAGING check under load, exactly
as `.claude/rules/test-suite.md` documents.

**`Netcode: net-sim` is a PRE-EXISTING flake on the base, and it flakes on an idle machine too.**
The base — a detached worktree of `playtest/2026-09-04-combined` @ `36724b4`, containing none of
this packet's changes — reds on the same assertion, on the same bot, with a **worse** magnitude than
this branch produced. The failure is not a threshold creeping: passing runs measure ~0.03–0.07 m and
failing runs measure **metres**, two orders of magnitude out, always on bot3, always one step. That
is a discrete snapshot/spawn seam landing inside the sampled window or not, not a busy machine.

Added to `.claude/rules/test-suite.md` as a measured entry, with the correction that re-running it
standalone does not settle it and the discriminator is the magnitude and the bot rather than the
verdict. **It is worth someone's packet** — a metre-scale remote jump under 5 % loss is a real
visible pop for a real player — but it is on `main`'s side of the fence, and nothing COPY-1 touches
is reachable from `scripts/net/**`.

**No other agent's Godot process was investigated or signalled** at any point, including while
SHADER-1's suite was live.

## Open questions

**None ripe.** The death-counter fork was withdrawn (Talon ruled it, Corrections §3), the panel
width was a value fork and was picked and stated, and the `TIDE` audit gap is a costed
recommendation rather than a fork — see Corrections §1 for the one-line shape and its one caveat.

Two things a later packet may want, neither blocking:

- **The `ToggleMode` trap is a `PillToggle` property, not a `SettingsPanel` one.** The durable fix
  is setting `ToggleMode = true` in `PillToggle`'s constructor rather than `_Ready`, which would
  make every future code-built pill immune anywhere in the game. `scripts/ui/PillToggle.cs` was
  outside this packet's file set, so the fix is at the three call sites and the guard is a source
  scan of one file. Any panel elsewhere that builds a `PillToggle` in code still has this.
- **`RichTextLabel` has no entry in the design system.** Talon's asterisks are the first request for
  inline emphasis in shipped copy. Adding the type to `UiThemeFactory` is a small, contained piece
  of work that would let this panel render his bold as bold.
