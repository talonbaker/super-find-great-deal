---
packet: BRAND-1
role: programming
date: 2026-09-04
branch: feat/2026-09-04-brand-1-wordmark-and-studio-mark
base: main @ c16414d
type: report
---

# BRAND-1 — the game has its name

`Branding.Wordmark` is `Watis World`. The splash shows `Great-Grand-Software`. Nothing else was
renamed, and the name-agnostic rule's guard (`DeadNameAuditTests`) is unmodified and green.

**EGG-2 acceptance criterion 10 — PASS.** Its text, verbatim from
`docs/agents/roles/programming/inbox/2026-09-02-EGG-2-surprises-in-the-bubble-level.md:241-242` on
`Sail`'s EGG-2 branch:

> 10. `Branding.Wordmark` is `Watis World` and the splash shows `Great-Grand-Software`; no other
>     string was renamed. **ABSENCE CHECK on the second half.**

Both halves are evidenced below — the positive half by the compiled constants, the source and a
headed capture; the absence half by two probes, each shown firing on a planted counterexample
before its all-clear was accepted.

---

## 1. What changed

Three files, plus this report and one `DECISION-LOG.md` section. The hunk came from `Sail`
`feat/2026-09-02-egg-2-surprises` @ `825e1087` against base `583ecc02`, applied as a hunk rather
than a file overwrite because this tree's copies had already diverged.

| File | Change |
|---|---|
| `scripts/ui/Branding.cs` | `Wordmark` → `"Watis World"`; new `Studio = "Great-Grand-Software"` with its doc comment; the 2026-08-14 placeholder paragraph replaced by the name-landed note |
| `scripts/ui/Splash.cs` | the studio-mark Label reads `Branding.Studio` instead of the literal `[ STUDIO MARK ]` |
| `tests/unit/UsageNoticeTests.cs` | `TheStudioMark_IsStillItsOwnScreen` now asserts the property, not the placeholder literal |

`git diff main --stat` on the finished branch:

```
 DECISION-LOG.md                                    |  75 ++++
 .../outbox/2026-09-04-BRAND-1-report.md            | 410 +++++++++++++++++++++
 docs/qa/BRAND-1/frame-02.png                       | Bin 0 -> 572287 bytes
 docs/qa/BRAND-1/frame-02.png.import                |  40 ++
 docs/qa/BRAND-1/frame-08.png                       | Bin 0 -> 990960 bytes
 docs/qa/BRAND-1/frame-08.png.import                |  40 ++
 scripts/ui/Branding.cs                             |  41 ++-
 scripts/ui/Splash.cs                               |  16 +-
 tests/unit/UsageNoticeTests.cs                     |  16 +-
 9 files changed, 618 insertions(+), 20 deletions(-)
```

Nine paths: the three source files, `DECISION-LOG.md`, this report, and the two committed capture
frames with their `.import` sidecars. Everything staged by path; no `git add -A`, and no
`.import`/`.uid` churn from the suite run was picked up.

No new test file was added — the packet's "any new pin" allowance went unused, because the right
change to `TheStudioMark_IsStillItsOwnScreen` was to fix the assertion it already made rather than
to leave a lock on the placeholder and pin the new value somewhere else. That is why the xUnit
total is unchanged at 1681 rather than higher.

**The splash keeps its open design question.** Both the class summary and the in-method comment
still record that a real studio logo IMAGE would replace the Label outright; the name fills the
slot, it does not close the question.

---

## 2. Acceptance criteria

### 1. `dotnet build WatisWorld.sln` — 0 errors

```
Build succeeded.
    4 Warning(s)
    0 Error(s)
```

All four warnings pre-exist on `main` (`AvatarClipDirector.cs:250` CS0618, `AvatarVisual.cs:2074`
and `:2090` CS8604, `SandboxCamera.cs:973` CS8602). None is in a file this packet touched.

### 2. The two constants, quoted

`scripts/ui/Branding.cs`:

```csharp
    public const string Wordmark = "Watis World";
```
```csharp
    public const string Studio = "Great-Grand-Software";
```

Asserted against the **compiled** assembly, not the source, in
`UsageNoticeTests.TheStudioMark_IsStillItsOwnScreen`:

```csharp
        Assert.Equal("Great-Grand-Software", MpFoundation.Ui.Branding.Studio);
        Assert.Equal("Watis World", MpFoundation.Ui.Branding.Wordmark);
```

### 3. The splash renders the studio name — headed capture

**Evidence: a headed capture, `docs/qa/BRAND-1/frame-02.png`.** Chosen over "the splash's own log
line" because the splash prints nothing — `Splash._Ready` sets `title.Text` and tweens; there is no
`GD.Print` on that path, and adding one purely to be quoted would be manufacturing the evidence.

The frame shows, in one shot, **both** halves of criterion 10's positive side:

- the splash Label reading **`Great-Grand-Software`**, over the loading bar and accent rule;
- the OS window title bar reading **`Watis World (DEBUG)`** — `Boot.cs:250`'s
  `DisplayServer.WindowSetTitle(Ui.Branding.Wordmark)`, so the wordmark is proven live too.

`docs/qa/BRAND-1/frame-08.png` carries the hand-off: the title screen with the wordmark
`Watis World` and `PRESS START` behind the playtester usage notice.

How it was taken: the game launched headed and windowed
(`Godot_v4.7-stable_mono_win64_console.exe --path C:\repos\Watis-brand1 --resolution 1280x720 --
--windowed` — engine flags before `--`, the game's `--windowed` after), with the primary screen
grabbed every 350 ms for ten frames, then the process stopped by PID. **No other agent's Godot
process was touched**; only the PID this capture started was stopped. Two of the ten frames are
committed (the splash beat and the title beat); the other eight were throwaway duplicates of the
same two states and were deleted rather than carried into the repo.

Two capture notes, both honest residue rather than findings:

- The first capture attempt was blocked by the crash-report dialog ("The game didn't close cleanly
  last time") — the leftover `session_active.marker` from that same attempt's own force-kill, in
  the shared `user://` profile. The capture script now deletes that marker before launching and
  again after stopping the process, so the profile is left exactly as it was found. Nothing else in
  the profile was read or written.
- Frames are full-screen grabs rather than window-cropped. A `GetWindowRect` crop was tried and
  produced a truncated image: the capture host is not per-monitor-DPI-aware, so the rect came back
  in 100%-scaled coordinates (1038×614) while `CopyFromScreen` worked in physical pixels. Full
  screen was simply correct, so the desktop behind the game window is in frame.

Stdout/stderr from that run were also captured and checked: no errors. The one stack trace on
stderr is a pre-existing engine **WARNING** — "Realtime Skies can only use a radiance size of 256",
from `MenuBackdrop.BuildWorld` at `scripts/ui/menu/MenuBackdrop.cs:305`. It is unrelated to
branding and present regardless of this change.

### 4. The ABSENCE half — no other string was renamed. PASS, with positive control.

Two probes, both run on the finished tree, both shown able to see a planted violation.

**Probe A — the file set.** `git diff main --stat` must list exactly `scripts/ui/Branding.cs`,
`scripts/ui/Splash.cs`, `tests/unit/UsageNoticeTests.cs`, `DECISION-LOG.md`, this report and the
capture PNGs.

**Probe B — the changed string literals.** Every added and removed line in `git diff main -U0` that
carries a `"` and is not a comment. On the finished tree:

```
+    public const string Wordmark = "Watis World";
+    public const string Studio = "Great-Grand-Software";
+        Assert.Contains("title.Text = Branding.Studio;", splash, StringComparison.Ordinal);
+        Assert.Equal("Great-Grand-Software", MpFoundation.Ui.Branding.Studio);
+        Assert.Equal("Watis World", MpFoundation.Ui.Branding.Wordmark);
```
```
-    public const string Wordmark = "WORKING TITLE";
-        title.Text = "[ STUDIO MARK ]";
-        Assert.Contains("[ STUDIO MARK ]", splash, StringComparison.Ordinal);
```

Two constants changed, plus the three assertions that check them. Nothing else.

**Positive control.** A rename was planted in a fourth file — `MainMenu.cs`'s node name
`Name = "Wordmark"` → `Name = "PlantedRename"` — and both probes fired:

```
PROBE A after plant:
 scripts/ui/Branding.cs         | 41 +++++++++++++++++++++++++++++------------
 scripts/ui/Splash.cs           | 16 ++++++++++------
 scripts/ui/menu/MainMenu.cs    |  2 +-
 tests/unit/UsageNoticeTests.cs | 16 ++++++++++++++--
 4 files changed, 54 insertions(+), 21 deletions(-)

PROBE B after plant:
+            Name = "PlantedRename",
```

The plant was then reverted and both probes returned to the clean output above. So the absence
claim rests on checks demonstrated to be capable of failing.

**Also proved able to fail: the new assertions themselves.** Two separate plants, each run against
the whole xUnit suite:

- `Branding.Studio = "Some Other Studio"` →
  `SailNet.Tests.UsageNoticeTests.TheStudioMark_IsStillItsOwnScreen [FAIL]`, `Assert.Equal()
  Failure` at `UsageNoticeTests.cs:534`. Raw line:
  `Failed! - Failed: 1, Passed: 1680, Skipped: 0, Total: 1681`.
- `Splash.cs`'s `title.Text = "[ STUDIO MARK ]"` restored →
  `TheStudioMark_IsStillItsOwnScreen [FAIL]`, `Assert.Contains() Failure` at
  `UsageNoticeTests.cs:530`. Raw line:
  `Failed! - Failed: 1, Passed: 1680, Skipped: 0, Total: 1681`.

Both plants were reverted; the suite returned to `Failed: 0, Passed: 1681`.

### 5. `DeadNameAuditTests` — present, unmodified, green

It is present on this tree at `tests/unit/DeadNameAuditTests.cs`.
`git diff main --name-only -- tests/unit/DeadNameAuditTests.cs` returns nothing: unmodified.

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 7 ms - SailNet.Tests.dll (net8.0)
```

(That filtered run is evidence about this one guard; the unfiltered totals are in criterion 6.)

The rule it guards was **satisfied, not repealed**: its own condition was "until a real title is
picked and cleared", and it sweeps for the dead 1983-film name, which neither `Watis World` nor
`Great-Grand-Software` contains. Its four tests still include its own positive control
(`PositiveControl_ScannerFindsAPlantedHit`) and its sweep-coverage check.

### 6. `dotnet test tests/unit/SailNet.Tests.csproj` — raw counts

This branch:

```
Passed!  - Failed:     0, Passed:  1681, Skipped:     0, Total:  1681, Duration: 3 s - SailNet.Tests.dll (net8.0)
```

Baseline, **measured** (not inherited) in a throwaway detached worktree at `main` @ `c16414d`:

```
Passed!  - Failed:     0, Passed:  1681, Skipped:     0, Total:  1681, Duration: 3 s - SailNet.Tests.dll (net8.0)
```

1681 → 1681. This packet edits an existing test rather than adding one, so the totals are identical
and nothing is owed a reconciliation. The baseline worktree was removed afterwards.

### 7. `tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180` — raw counts

Run in the foreground, redirected (not piped) to
`…/scratchpad/brand1-suite.txt`, outside `tests/logs/`, then read back from the file. The lock was
uncontended. The run printed an `OVERALL:` line, so it is a real result and not a mutex timeout.

**Raw counts, from the run's own `=== summary ===` block: 40 suites, 39 PASS, 1 FAIL.**

```
OVERALL: FAIL
```

The single red:

```
  Bubbles: shared counter    FAIL
```

```
>>> Bubbles: shared counter
[4/4] verifying the shared tally across three peers...
        lever: accepted=1 refused=1 resets broadcast=1
        lever: warnings armed=2
        lever: stray pulls armed=2 accepted=0 tally 2 -> 2
        A (walker)   samples=176  worst bob offset=0.1356 m
        B (witness)  samples=176  worst bob offset=0.1356 m
        C (late)     samples=128  worst bob offset=0.1350 m

BUBBLE SYNC TEST FAILED (1 failure(s)):
  - C (late) never saw the reset return the tally to 0 and every bubble to visible
```

**Discrimination against `main` — this is main's own red, not a regression, and not a STOP.**

1. **It is already a recorded finding on this tree.** `DECISION-LOG.md` §4 (2026-09-02, the first
   Windows machine) names this exact suite and this exact check: *"`Bubbles: shared counter` —
   deterministic, and **not a defect; deliberately not fixed**. The late joiner exits 120 ms before
   the reset broadcast … `$LateDurationSec = 26` in `tests/Run-BubbleSyncTest.ps1` is simply a
   knife-edge window. Left for the testing pass rather than tuned green here."* That testing pass is
   FIX-1, whose branch is not merged, so `main` still carries the red.
2. **Measured, not reasoned.** `tests/Run-BubbleSyncTest.ps1` was run standalone on an idle machine
   (the marathon had finished; nothing else was running) on **both** trees:
   - `main` @ `c16414d`, in a detached worktree: **FAIL**, `C (late) never saw the reset return the
     tally to 0 and every bubble to visible`, `A 176 / B 176 / C 128` samples, worst bob offset
     `0.1356 / 0.1356 / 0.1350 m`.
   - this branch: **FAIL**, same failing check name, and the **same measured quantities** —
     `A 176 / B 176 / C 128`, `0.1356 / 0.1356 / 0.1350 m`, `accepted=1 refused=1 resets
     broadcast=1`.

   Comparing the raw quantity rather than the verdict, the two runs are indistinguishable.
3. **My diff cannot reach it.** The only C# changed is two `const string` values in
   `scripts/ui/Branding.cs` and one line in `scripts/ui/Splash.cs`. Nothing here touches
   `BubbleManager`, the run spine, replication, the reset broadcast, or `Run-BubbleSyncTest.ps1`.
   `Branding.Studio` has exactly one reader (the splash Label) and `Branding.Wordmark` three
   (`MainMenu.BuildWordmark`, `Boot`'s window title, `DeadNameAuditTests`); none is on the bubble
   path, and the suite runs headless where the splash never builds.

The packet's STOP condition is "any suite red that **main passes** and your diff can reach". Main
does not pass it and the diff cannot reach it, so neither half holds.

The other 39 suites — including the four `.claude/rules/test-suite.md` names as load-flaky
(`World: run driver`, `World: tidal-cycle phase`, `Carry: regrab-while-loose`,
`Reconnect: grace window`) and `Netcode: anti-cheat` — all passed on the first marathon.

### 8. This report

You are reading it.

---

## 3. Every reference to a cut system that was removed

The precedent is MOVE-1's, `DECISION-LOG.md` "2026-09-04 — the lab and the bike come over": where a
ported comment names a surface the 2026-09-02 extraction cut, the reference goes, and the system is
never re-imported.

1. **`Branding.cs` class summary — "campfire plank sign".** `Sail`'s ported line lists the wordmark
   surfaces as "(splash, title screen, main menu, campfire plank sign, window title, crash/feedback
   dialogs)". The campfire sign went with the camp economy (`DECISION-LOG.md` §2.3–2.5). Removed;
   the ported line reads "(splash, title screen, main menu, window title, crash/feedback dialogs)".
   This tree's pre-port copy had already dropped it from the same list, so the removal restores
   consistency rather than inventing it.
2. **`Branding.Wordmark`'s new doc comment — "the campfire plank sign".** `Sail`'s text: "as shown
   on the splash's hand-off, the title screen, the campfire plank sign and the OS window title".
   Removed. It now reads "the splash's hand-off, the title screen and the OS window title" — which
   is the true live set: `Splash` → `ScenePaths.Title`, `MainMenu.BuildWordmark`, and `Boot.cs:250`.
3. **The whole 2026-08-14 placeholder paragraph, carrying this tree's own softened `PlankFont`
   residue.** Before the port, this repo's `Branding.cs` still justified `WORKING TITLE` by "the
   since-removed plank font", "that font's documented D/O and V/U glyph collisions" and "the old
   sign's normal path". `Sail`'s hunk deletes that paragraph and replaces it with the name-landed
   note, so the residue goes out with it. Neither `Campfire.PlankFont` nor the sign was re-imported.

Nothing was put back. `Splash.cs` and `UsageNoticeTests.cs`'s ported comments name only surfaces
this tree has: `Branding.Studio`, `ScenePaths.Title`, and the splash's own Label.

**One cut-system reference deliberately left alone.** `tests/unit/DeadNameAuditTests.cs`'s summary
still describes `Branding.Wordmark` as what "(window title, title screen, campfire plank sign,
menus)" read from. It is stale in exactly the same way — but that file passing **unmodified** is
acceptance criterion 5, and editing it to tidy a comment would have forfeited the criterion for
nothing. It is pre-existing residue this port did not introduce. Whoever does the deliberate
residue pass should take it.

---

## 4. Corrections to the packet

1. **The packet's baseline figure was right; the reason given for distrusting it was not quite the
   situation described.** Per the dispatcher's amendment I measured `main` @ `c16414d` myself
   instead of inheriting 1681 — and got exactly `Failed: 0, Passed: 1681, Skipped: 0, Total: 1681`.
   On the "wrong figure in the outbox": the report in question is
   `docs/agents/roles/programming/outbox/2026-09-04-FIX-1-report.md` (on branch
   `fix/2026-09-04-fix-1-bubble-late-joiner-window`, line 322), which quotes
   `Passed: 1687, Total: 1687`. Read in context that is **not** a claim about main's baseline — the
   next line says "That total includes the 6 new pins in `tests/unit/BubbleSyncWindowTests.cs`",
   and its diff does add exactly that file. 1681 + 6 = 1687. FIX-1's number is correct *for FIX-1's
   branch*; the hazard is only that a reader who lifts 1687 out of it would mistake it for main's.
   Measuring beats quoting either way.

2. **`main` is one commit ahead of `origin/main`.** After `git fetch origin`, local `main` is
   `c16414d` while `origin/main` is `dedc0f3`. `c16414d` ("Name the Sail-pointing references as
   provenance; fix one broken ROLE path") has not been pushed. My branch is based on local `main`,
   so pushing it carries `c16414d` up with it. Flagging rather than acting: I did not push `main`
   itself and did not touch `C:\repos\Watis_Game`'s working tree.

3. **`docs/agents/roles/programming/outbox/` did not exist on `main`** — every 2026-09-04 packet
   creates it on its own branch. Created here too; harmless, but the four branches in flight will
   all "add" the same directory.

4. **The packet's PR #2 finding is confirmed.** `git diff --name-only c16414d..260a5b2 --
   scripts/ui/Branding.cs scripts/ui/Splash.cs tests/unit/UsageNoticeTests.cs` is empty. PR #2 never
   owned these files; LEVEL-1 was told wrongly and excluded the hunk correctly; the work was an
   orphan until this packet.

---

## 5. Open questions

1. **The studio name's provenance, and a different open thread that is *not* it.** The name comes
   from Talon's easter-egg addendum §10 dated 2026-09-02, quoted verbatim in the EGG-2 source
   ("change to show the studio name, Great-Grand-Software"). That is the authority acted on, and it
   is sufficient. Separately, `C:\repos\HANDOFF-2026-09-02-FABLE_STONECREST.md` records at line 8
   that the Great-Grand-Software **GitHub org** "is not part of this project and contains none of
   its code" (three unrelated repos — FrogBall, Blueberry, godot-web-template — untouched since
   Aug 24), and at lines 27 and 63 that both project repos live under `talonbaker` with
   "keep or transfer" left explicitly as Talon's open decision. Which org hosts the code is a
   different question from what studio name appears on a splash screen; the second does not settle
   the first, and the first does not block the second. Recorded here so a future reader finds the
   thread rather than treating this splash screen as the ruling. **No code, comment or document was
   edited on the strength of the org question.**

2. **Three store documents now describe a wordmark the build no longer has.**
   `docs/store/store-description.md:102,130`, `docs/store/PLAYTEST-SETUP-CHECKLIST.md:102-105` and
   `docs/store/2026-08-29-STEAM-STATE-private-playtest.md:22,42` all state, some of them as a
   verified finding citing `Branding.cs:19`, that the game renders `WORKING TITLE`. As of this
   commit that is false. `docs/store/**` is outside this packet's allowed paths and outside its
   scope ("any other string ... is out of scope"), so nothing was changed — but this is a direction
   question worth one line from Talon: the Steamworks **Store Name** field and these three documents
   are now the only places still carrying the placeholder, and the store name is his call, not a
   mechanical follow-on from the in-game wordmark.

3. **`Branding.WordmarkImagePath` is still unsupplied**, checked with `ResourceLoader.Exists` and
   falling back to the text wordmark — deliberately untouched, per the packet. Now that the title
   is real, the title-screen graphic is a live ask on Talon rather than a placeholder waiting on a
   name.

---

## 6. The bible check

```
Bibles applied:  none.
Items checked:   none.
Result:          n/a — this is not a gameplay feature. It changes two branding string constants
                 and the screen that displays one of them. Nothing here moves or acts on its own
                 (Behavior), resolves state, timers or win/loss (Mechanics), is acted on by the
                 player (Interaction), or arranges a playable space (Level). UI-DESIGN-SYSTEM.md
                 was read and is respected: the splash Label keeps its `Hero` theme type variation
                 and holds no appearance of its own — only its text changed.
```

---

## 7. Stop conditions — none hit

- The hunk applied without touching a file outside the three. (`MainMenu.cs` was modified only as
  the deliberate positive-control plant, and reverted.)
- The dead-name audit did not go red.
- Suite reds, if any, are discriminated in §2 criterion 7 against `.claude/rules/test-suite.md`.
