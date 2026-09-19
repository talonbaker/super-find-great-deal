# AVATAR-1 — one body for the first build: the Box Kid

**Role:** programming (`PROG-KESTREL-58`) · **Date:** 2026-09-05
**Branch:** `feat/2026-09-05-avatar-1-one-body` · **Base:** `playtest/2026-09-04-combined`
**Worktree:** `C:/repos/Watis-avatar1` (never touched `Watis_Game` or `Watis-playtest`)

> Talon: *"the player can choose a different model. There's a gray box in the box, kid, and the
> puffing designs of player character that they can be. I need this removed. I need this to only
> be using the main model the main player character model that we have agreed on which is the one
> that is default selected I don't want players to be able to change for this initial build
> because of the movements don't line up right."*

**Done.** A player in this build cannot select, or end up as, anything but the Box Kid. Both menus
lost the picker. The roster, the replication path and every dev/test route are untouched.

---

## What changed — four files, plus one new test file

| File | Change |
|---|---|
| `scripts/ui/HostMenu.cs` | Dropped `_avatarPrev/_avatarNext/_avatarLabel/_avatarIndex`, `CycleAvatar`, `RefreshAvatarLabel`, the three `GetNode` lookups and the two `Pressed` handlers. **Deleted the `net.LocalAvatarKey = …` write** and replaced it with the reasoning comment. Dropped the now-unused `using MpFoundation.Game.Sandbox`. |
| `scripts/ui/JoinMenu.cs` | Identical treatment. |
| `scenes/ui/HostMenu.tscn` | Removed `Column/AvatarCap`, `Column/AvatarRow` and its three children; collapsed `Gap1b`+`Gap2` to the one 24 px section gap. |
| `scenes/ui/JoinMenu.tscn` | Identical. |
| `tests/unit/OneBodyForTheFirstBuildTests.cs` | **New.** Six tests: the absence claim, its positive control, the node-name absence, its positive control, the unset-resolves-to-boxkid claim, and the peer wire table. |

Doc-only touch-ups where prose still described the picker as live, invariants **kept**:
`scripts/game/sandbox/AvatarVisual.cs` (`RosterEntries`), `scripts/game/sandbox/SandboxSelfTest.cs`
(`the_pickers_default_row_builds_the_played_body`), `tests/unit/BoxKidAvatarTests.cs`
(`ThePickersDefaultRowIsTheDefaultBody`).

### Scope item 2 — how the body is forced, and why this way

**`LocalAvatarKey` is left UNSET.** The menus no longer write it at all.

That is the packet's preferred option and it is the right one. `""` already had a defined meaning
— *nobody chose* — and already resolved through `SandboxAvatar` →
`AvatarVisual.ResolveEnvAvatarKey(world)` → `PreferredAvatarKeyFor` → `PreferredAvatarKey` =
`BoxKidAvatarKey`. Writing `"boxkid"` at the two call sites instead would have created a **second**
source of truth for the played body, and two disagreeing sources is precisely the bug BODY-1 was
dispatched to fix (Talon played a whole session on the gumdrop because only the fallback moved).
Restoring the picker later is restoring two assignments, not re-deriving a mechanism.

`NetworkManager.LocalAvatarKey`, its reader in `SandboxAvatar`, and the whole replication path stay
exactly as they were.

### Scope item 4 — the layout

Removing the row left `NameEdit → Gap1b(12) → Gap2(24) → …`, i.e. a doubled 36 px gap. I kept
**`Gap2` (24)** and dropped `Gap1b` (12): `Gap2` was the form-to-next-section gap (`Gap3` before the
action button is the same idea), while `Gap1b` existed only as the AVATAR caption's breathing room
and had nothing left to breathe against. No token, recipe, variation or `UiLayers` rung was touched;
nothing was redesigned. See the captures.

---

## Acceptance criteria

**1. Build.** `dotnet build WatisWorld.sln` → **0 Errors**, 4 Warnings (the four pre-existing names
`DECISION-LOG.md` §4 already records — `SandboxCamera`, `AvatarClipDirector`, two in `AvatarVisual`).

**2. Captures.** `docs/qa/AVATAR-1/before/` and `docs/qa/AVATAR-1/after/`, four headed frames at
1600×900, all four taken through the **shipped `DevScreenshot` F9 path** (real in-engine viewport
reads, not desktop grabs — no capture code was added to the game):

| | before | after |
|---|---|---|
| host | `AVATAR` caption + `‹ Box Kid ›` present | caption and row gone; `YOUR NAME` → field → gap → `Start Hosting` |
| join | same picker, above `ROOM CODE` | gone; `YOUR NAME` → field → `ROOM CODE` → field → direct-connect toggle → `Join` |

Both columns stay centred and evenly spaced. `--capture-cam` does not apply — these are 2-D menu
screens with no 3-D camera; the equivalent determinism came from launching the menu scene directly.

**3. The launched game spawns the Box Kid — from the log, not the screenshot.** `SAIL_AVATAR`
verified unset (exactly the state a player leaving the menus is now in), headless launch,
`-- --practice --world bubbletest`:

```
[avatar] key 'boxkid' resolved to AuthoredRig <- res://assets/creatures/boxkid/BoxKid.glb
         (declared absent: Belly, Tail, BackFiller, BlushL, BlushR, Stem, LeafL, LeafR)
```

Both spawned avatars resolved that same line; no other `[avatar] key …` line appeared. Exit 0.

**4. ABSENCE check, with a positive control.**

*Every remaining mention of `LocalAvatarKey` in the tree, and what each is for:*

| Site | Kind | What it is |
|---|---|---|
| `scripts/NetworkManager.cs:72` | **declaration** | `public string LocalAvatarKey { get; set; } = ""` — the property itself. Kept. |
| `scripts/game/sandbox/SandboxAvatar.cs:1182-1184` | **read** | the three-tier resolve. With no writer left, tier 1 is always empty and it falls to `ResolveEnvAvatarKey`. |
| `scripts/game/sandbox/AvatarVisual.cs:1292-1294` | **comment** | BODY-1 history. |
| `scripts/game/sandbox/SandboxSelfTest.cs:1073` | **comment** | ditto, now noting the mechanism is dormant. |
| `tests/unit/BoxKidAvatarTests.cs:158-159` | **comment** | ditto. |
| `tests/unit/OneBodyForTheFirstBuildTests.cs` | **test** | the probe and its control. |

**Writers: none.** The two former writers (`HostMenu.cs:250`, `JoinMenu.cs:89`) are deleted. There
is no `--avatar` launch flag and no `settings.cfg` persistence of an avatar choice — grepped both.
The only remaining way to select a body is `SAIL_AVATAR`, a dev/test path (see criterion 5).

*Positive control, in-test:* `TheLocalAvatarKeyProbeCatchesAPlantedStray` feeds the regex five
stray shapes (`= `, `??=`, extra whitespace, bare receiver, qualified receiver) and three reads,
and asserts it flags all five and none of the three.

*Positive control, planted in a scratch run* — I put a real stray back into the working tree
(`net.LocalAvatarKey = "greybox_classic";` in `HostMenu.OnPlayPressed`) **and** an orphaned
`AvatarRow` node into `JoinMenu.tscn`, then ran the suite:

```
Failed!  - Failed: 2, Passed: 4, Skipped: 0, Total: 6

AVATAR-1: these shipped files write NetworkManager.LocalAvatarKey, so a player can
reach a body that is not the box kid:
  scripts\ui\HostMenu.cs
AVATAR-1: picker leftovers in the menus:
  scenes/ui/JoinMenu.tscn: AvatarRow
```

Both plants were reverted and the six went green again. The probes catch a stray and name it.

**5. `tests/Run-AvatarIdentityTest.ps1` — how it now drives the choice.**

**It never used the UI, so nothing about it changed.** It drives all three peers through the
**`SAIL_AVATAR` environment variable** on headless `--bot` launches: `AvatarBotA` gets
`greybox_classic`, `AvatarBotB` (the late joiner) `boxkid`, `AvatarBotC`
`totally-bogus-not-a-roster-entry` to pin the clamp. Those bots never load `HostMenu`/`JoinMenu`
and never touched `LocalAvatarKey` — they always went down the `ResolveEnvAvatarKey` path, which
this packet did not modify. **No STOP condition was reached.** Result below.

**6. No orphaned node paths.** Both menu scenes launched headed on the tip:

```
HostMenu : ERROR lines = 0 ; SCRIPT ERROR = 0 ; node-not-found = 0
JoinMenu : ERROR lines = 0 ; SCRIPT ERROR = 0 ; node-not-found = 0
```

`grep -rn "AvatarRow\|AvatarPrev\|AvatarNext\|AvatarLabel\|AvatarCap"` over the tree returns
**nothing** outside `tests/unit/OneBodyForTheFirstBuildTests.cs`, where the names are the probe's
own needles.

**7. Both suites — see "Suite results" below.**

**8. This report** + the `DECISION-LOG.md` section — see "Corrections to the packet".

---

## Suite results

*(RAW counts, from each run's own summary block.)*

### `dotnet test tests/unit/SailNet.Tests.csproj`

| Tree | Runs | Result |
|---|---|---|
| **Baseline** (throwaway detached worktree `C:/repos/Watis-avatar1-baseline` at the same commit) | 4 | `Failed: 0, Passed: 2288, Skipped: 0, Total: 2288` ×4 |
| **This branch** | 6 | 1 aborted (below), then `Failed: 0, Passed: 2294, Skipped: 0, Total: 2294` ×5 |

**2288 → 2294 is exactly the six tests this packet adds.** Zero failures on either side.

**The one abort, discriminated rather than waved through.** The very first run on this branch
(immediately after a fresh build) ended `Test Run Aborted` with:

```
Test host process crashed
Fatal error. System.AccessViolationException: Attempted to read or write protected memory.
   at Godot.NativeInterop.NativeFuncs.godotsharp_string_new_with_utf16_chars(...)
   at Godot.StringName..ctor(System.String)
   at Godot.Engine..cctor()
   at Godot.Engine.GetMainLoop()
   at MpFoundation.Net.MotorTuning.SessionLive()
   at SailNet.Tests.AnticipationCoilTests.TheLiveGravityForIsUntouchedAtModeOne()
```

That is a static-constructor race in **Godot's marshalling layer**, reached through
`MotorTuning.SessionLive()` from `AnticipationCoilTests` — a motor-tuning test. Nothing AVATAR-1
touches is on that stack: this packet changes two menu scripts, two menu scenes and adds a test
class that does file I/O and reads `AvatarVisual` string constants. It did not reproduce in five
subsequent runs on the same tree. Recorded as a measured entry in `.claude/rules/test-suite.md`.

### `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`

**RAW: 48 rows / 47 PASS / 1 FAIL — `OVERALL: FAIL`.** Counted from the run's own
`=== summary ===` block with the anchored `^  .+ (PASS|FAIL)$`.

The one red is **`World: tidal-cycle phase`**. `Avatar identity: replication` — the suite this
packet's AC5 turns on — **PASSED**.

**The packet told me not to wave this through as a known flake, so I did not.** The AC7 note cites
ROOFTOP-1 measuring 48/48 on this base, and `.claude/rules/test-suite.md`'s BUBBLE-2 entry says
this suite passes 3/3 standalone on an idle machine. Both are true and both are misleading, and
re-running my own branch would not have settled it. So I A/B'd it against the base:

| Tree | Runs (standalone, verified-idle) | Result | Worst cross-view divergence (tol 1.5 s) |
|---|---|---|---|
| This branch | 3 | PASS, **FAIL**, PASS | 1.5250 s |
| **Unmodified base** (detached worktree, same commit) | 5 | PASS, **FAIL**, PASS, PASS, **FAIL** | **2.3781 s**, 2.0325 s |

**The unmodified base fails this suite more often and more severely than this branch does.** The
red is pre-existing and load-independent, and AVATAR-1 cannot be its cause: this packet touches two
menu scripts, two menu scenes and one test file, and the failing assertion compares two live
clients' day/night phase clocks. In the marathon the overshoot was 1.5247 s against a 1.5 s
tolerance — **24.7 ms, 1.6% over, on 3 of 150 matched pairs** — while the same run's late-join
(0.032 s) and rejoin (0.0011 s) checks passed with three orders of magnitude of headroom.

`Get-Process` showed **zero** Godot processes before each of those eight runs; nothing else of mine
was running. During the marathon itself I did not have exclusive use of the machine and cannot
claim otherwise.

Recorded as a measured entry in `.claude/rules/test-suite.md` — including the finding that
BUBBLE-2's "idle ⇒ green" conclusion is too strong, and that **A/B against the base**, not
re-running on an idle machine, is what actually discriminates this suite.

---

## Scope item 3 — what a peer sending another body now produces

Asked and answered explicitly, because the packet asks for it stated rather than inferred.

| On the wire | Resolves to | Reachable by a player of this build? |
|---|---|---|
| `boxkid` | `boxkid` | **Yes — and it is the only thing they can be.** |
| `greybox_classic` | **`greybox_classic`** — itself, unchanged | **No.** |
| `greybox_primitive` | itself | **No.** |
| `greybox` (retired gumdrop) | `greybox_classic`, aliased forward | **No.** |
| anything else set and unknown | `greybox_primitive` (`DefaultAvatarKey`, the containment clamp) | **No.** |

**A peer sending `greybox_classic` still renders as the classic greybox on every peer.** That row
is live, `NormalizeAvatarKey` passes it through, and AVATAR-1 deliberately did not change that —
weakening the clamp or collapsing the table would have broken `Run-AvatarIdentityTest` and thrown
away a real containment property for no gain.

**What changed is that no client built from this tree can send it.** With both writers of
`LocalAvatarKey` gone, reaching `greybox_classic` on the wire requires either `SAIL_AVATAR` in the
process environment (a dev/test path — `GreyboxPlayerLab`, the movement playground,
`Run-AvatarIdentityTest`) or a client that is not this build (an older build, or a hostile one).
Neither is a player of this build using the shipped UI. A hostile peer choosing another body
affects only how **that** peer's own avatar renders; it cannot change anyone else's.

---

## Bible check

```
Bibles applied:  INTERACTION-BIBLE (the removed control), ART-BIBLE (the body that stays)
Items checked:   INTERACTION §1 Affordance, §2 Feedback, §6 Reversibility, §5 Multiplayer
                 Contention; ART §6 Character construction
Result:          pass
```

- **INTERACTION §1 (Affordance).** The failure mode when deleting a control is the *residue*: a
  button with no handler, or a caption over nothing. Both were checked and both are gone — I
  removed the `AvatarCap` "AVATAR" label as well as the row (see Corrections), the scenes carry no
  leftover nodes, and both menus open with 0 ERROR lines. Nothing on either screen now affords a
  choice that does not exist.
- **INTERACTION §2 (Feedback).** N/A by removal — there is no longer an input to acknowledge.
- **INTERACTION §6 (Reversibility).** The picker was freely repeatable and its state was never
  persisted. Removing it leaves no state to reset: `LocalAvatarKey` is `""` on every launch, relog
  and respawn, so every session resolves the same body idempotently. Nothing needs a migration.
- **INTERACTION §5 (Multiplayer contention).** Improved trivially: every peer of this build now
  presents the same key, so there is no contention left to resolve.
- **ART §6 (Character construction).** The shipped body is unchanged — `BoxKid.glb`, its rig, its
  proportions, its `HeadIsDistinctVolume: true` claim and the §3 tonal band that lands on its neck
  step are all untouched. This packet chooses *which* body ships, not *what* it looks like.
- **MECHANICS-BIBLE** was considered and does not apply: no state machine, timer, boundary or
  race was added or removed. The three-tier resolve already existed; this packet only stopped
  writing to tier 1.

---

## Corrections to the packet

1. **`AvatarCap` was not on the packet's list, and had to go too.** The packet named
   `Column/AvatarRow/...`. Above that row sat `Column/AvatarCap`, a `FieldLabel` reading
   **"AVATAR"**. Removing only the row would have left a section caption standing over nothing —
   worse than the control it captioned, and a direct INTERACTION §1 miss. Removed in both scenes
   and added to the test's node-name list. Flagged rather than done silently because it is one node
   beyond the packet's letter.

2. **Two spacers collapsed into one.** Also not in the packet's letter, but "leave the layout sane"
   (scope item 4) required it: `Gap1b` and `Gap2` were adjacent after the removal. Reasoning above.

3. **`DECISION-LOG.md` is outside this role's allowed write paths, so I did not write it.**
   AC8 requires a `DECISION-LOG.md` section. `ROLE.md`'s allowed-paths list does not include the
   repo-root `DECISION-LOG.md`, and it says plainly: *"Writing outside these paths is not
   self-corrected — stop and report it to the orchestrator as a failure. If the packet seems to
   require it, the packet is wrong; say so in the report."* CARRY-1 hit the same shape on
   2026-08-29 and flagging was recorded as the right call, so I flagged rather than self-corrected
   across it. **The section is written out below, ready to paste**, and one line from Talon or the
   orchestrator either grants the path or lands it for me.

4. **The base moved under me mid-packet, and I merged it forward.** I branched at `3e3ac77`;
   ROOFTOP-1 then merged into `playtest/2026-09-04-combined`, taking the tip to `0385753`. The
   packet's AC7 note cites the 48/48 marathon *"on this base"*, which is the post-merge base, so I
   merged the tip in (fast-forward, zero file overlap — ROOFTOP-1 is level/world, this is menus)
   and ran the suites on that. Worth knowing that the combined branch is live during a wave.

5. **No STOP condition was reached.** All three were checked: `Run-AvatarIdentityTest` drives the
   choice through `SAIL_AVATAR` and never needed the UI; no roster row was deleted; and the Box Kid
   is what actually spawns (criterion 3's log line).

---

## For `DECISION-LOG.md` — ready to paste

```markdown
## 9. AVATAR-1 — one body for the first build, and what has to be true before the picker returns (2026-09-05)

Talon, on the last change before the Steam playtest upload: *"the player can choose a different
model... I need this removed. I need this to only be using the main model the main player character
model that we have agreed on which is the one that is default selected I don't want players to be
able to change for this initial build **because of the movements don't line up right**."*

**This is a FIRST-BUILD restriction taken for an animation-fit reason. It is not a decision about
the roster, and it is not permanent.**

### What the reason actually says

The clip library is authored against one set of proportions. `BoxKid.glb`'s pivots are the
greybox's pivots to the millimetre — `assets/creatures/boxkid/build_boxkid.py` *imports*
`build_greybox.py`'s joint heights rather than copying them — so the same fourteen NLA clips bind
to it verbatim. `greybox_classic` is a different body: a separate head frustum on a differently
proportioned torso, built in code. The clips play on it, but they do not *fit* it. Talon saw that
and removed the choice rather than shipping a body whose animation reads wrong.

So the risk is the OTHER bodies, not the Box Kid — which is why the fix is to remove the choice,
not to change the default.

### What was done, and deliberately not done

- **Removed:** the picker from `HostMenu`/`JoinMenu` — the `‹ ›` controls, the `AVATAR` caption,
  the cycle handlers, and both unconditional writes into `NetworkManager.LocalAvatarKey`.
- **Not removed:** the roster (`greybox_primitive` is still the load-failure fallback and the
  engine-free xUnit fixture; `greybox_classic` is still the last code-built player-shaped body and
  the control the suite measures the authored one against), `LocalAvatarKey` and its reader, the
  roster-choice replication path, the wire-key table and its containment clamp, `SAIL_AVATAR`, and
  the dev/lab body switching.
- **Mechanism, not a second constant:** `LocalAvatarKey` is left UNSET so the pre-existing
  `ResolveEnvAvatarKey() → PreferredAvatarKeyFor → PreferredAvatarKey` path resolves the body.
  Writing `"boxkid"` at the menus would have made a second source of truth, and two disagreeing
  sources is exactly the BODY-1 bug.

### Before the picker comes back, these must be true

1. **The clips fit every body on offer.** ART-BIBLE §6.7's bone-placement gate and §6.8's clip
   validation contract are the existing instruments; they need to pass for each selectable body,
   not just the played one.
2. **Index 0 is still the default body.** `ThePickersDefaultRowIsTheDefaultBody` (xUnit) and
   `the_pickers_default_row_builds_the_played_body` (`SandboxSelfTest`) were **kept green and
   deliberately not deleted** for this reason. A restored picker writes
   `RosterEntries[_avatarIndex].Key` into a value that BEATS the world's preferred key, so index 0
   and `PreferredAvatarKey` must remain the same symbol.
3. **`OneBodyForTheFirstBuildTests` is the thing to delete**, consciously and in the same commit
   that restores the picker. It asserts that nothing writes `LocalAvatarKey`; restoring the picker
   is exactly what makes it fail, and that failure is the intended signal, not a regression.
4. **The layout has room again.** Both menu columns had `AvatarCap` + `AvatarRow` + a 12 px spacer
   removed and the two spacers collapsed to the one 24 px section gap.

### Measured

    dotnet build WatisWorld.sln              0 Errors, 4 Warnings (the four §4 names)
    dotnet test SailNet.Tests.csproj         2288 -> 2294 passed, 0 failed (+6, this packet's)
    both menus launched headed               0 ERROR lines each
    headless --practice --world bubbletest   [avatar] key 'boxkid' resolved to AuthoredRig
                                             <- res://assets/creatures/boxkid/BoxKid.glb
```

---

## Open questions

**None ripe.** One thing worth Talon's eye eventually, not now and not a blocker: with the choice
gone, the `AVATAR` field on both menus is the only form row that ever existed purely to be chosen
from. If a body picker never comes back, `RosterEntries`' display names (`"Box Kid"`, `"Greybox"`,
`"Greybox (primitive)"`) are read by nothing player-facing any more — they survive as lab and test
labels. Not worth a packet; worth knowing when someone next reads that array and wonders who it is
for.
