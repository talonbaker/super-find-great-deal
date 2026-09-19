# STEAM-1 — `watis_game.exe`, and the real App ID

**Role:** programming — **Packet:** STEAM-1 — **Date:** 2026-09-04
**Branch:** `feat/2026-09-04-steam-1-app-id-and-exe-name`, worktree `C:\repos\Watis-steam1`
**Base:** `playtest/2026-09-04-combined` @ `1f4d688`

Talon, from the Steamworks partner site:

> "The following updates were made. The install folder is now called `watis_install_folder` and
> the executable is called `watis_game.exe` This is the number, by the way 4951240. I've
> requested playtest keys."

The repo now agrees with all three.

---

## Corrections to the packet

1. **Allowed-write-paths gap (three files).** `docs/agents/roles/programming/ROLE.md` grants
   `scripts/**`, `tests/**`, `assets/run/**`, `deploy/**`, `scenes/**` (named only),
   `project.godot` (when the packet says so), `docs/qa/**`, `.claude/rules/test-suite.md` and the
   outbox. **`export_presets.cfg`, `docs/store/**` and `DECISION-LOG.md` are on none of those
   lists**, and STEAM-1 requires all three by name (scope 1, scope 5, acceptance 8). I wrote them
   — the packet names each explicitly and the work is impossible otherwise — and am flagging it
   rather than self-correcting across it, per the CARRY-1 precedent the ROLE file records. The
   paths list has simply not caught up with what programming packets are routinely asked for:
   `export_presets.cfg` is stamped by `deploy/Export-WindowsClient.ps1` as "a generated mirror"
   of `BuildInfo.cs` and is arguably already part of `deploy/**` in practice, and every packet
   this month has been required to write a `DECISION-LOG.md` section. Three lines from Talon
   closes it.
2. **The packet cites `export_presets.cfg:46` for the Windows `export_path`. Correct on the base,
   and it is `:12` and `:94` for Linux and macOS as stated** — but the file is rewritten in place
   by the export script's version stamp on every run, so line numbers there are not durable
   citations. Not a defect in the packet; worth knowing before quoting a line number at it.
3. **The packet says the app id "is NOT committed anywhere and that is deliberate."** True, and
   it stays true. But `4951240` is *already* in the tree in three places on the base branch —
   `docs/store/2026-08-29-STEAM-STATE-private-playtest.md` §4/§4c and
   `docs/store/PLAYTEST-SETUP-CHECKLIST.md` §1/§2/§6 — where it is recorded as the **Playtest
   app** and, in the checklist, as the number **not** to use. "Never committed" is true of the
   SteamPipe templates and of code; it was never true of the store docs. See the open question.
4. **`docs/STEAM.md` does not exist.** `scripts/net/steam/SteamService.cs:22` says "See
   docs/STEAM.md" for the App ID resolution path. That is a reference to a path that should exist
   *here* and does not — reported per `CLAUDE.md`, not fixed (it is in `scripts/`, and the
   packet's scope does not reach it). The content it points at now lives in
   `deploy/steam/README.md` and `2026-08-29-STEAM-STATE-private-playtest.md` §3b.

Otherwise: none. The five scope items and the eight acceptance criteria were as described.

---

## 1. The Windows executable is `watis_game.exe`

Renamed in the three places that have to agree, plus every check, log line, comment and doc that
named the old binary:

| File | What moved |
|---|---|
| `export_presets.cfg:46` | `[preset.1] export_path` |
| `deploy/Export-WindowsClient.ps1:21` | `$exportBin` — what every post-export assertion reads |
| `deploy/Export-WindowsClient.ps1:120` | the CLI argument to `--export-release` |
| `deploy/steam/Upload-Steam.ps1:9,47` | the doc-comment prerequisite and `$clientExe` |

### Would a half-rename have failed loudly, or passed vacuously? Both — and which is which matters

The packet asked. The three sites fail differently:

- **`$exportBin` or the CLI argument renamed alone → FAILS LOUDLY.** `Export-WindowsClient.ps1`
  wipes `build/windows-client` before every export, so no stale binary can satisfy
  `Test-Path $exportBin`; the script stops at *"export produced no binary at ..."*.
- **`export_presets.cfg` renamed alone → PASSES VACUOUSLY.** This is the dangerous one. The
  script passes the output path as a positional argument to `--export-release`, and **that
  argument overrides the preset's `export_path`**. So the preset can say `watis_game.exe` while
  the export emits `watis-world.exe`, `$exportBin` finds it, and the script prints
  `[export] OK` — a green export whose binary does not match the name Steam was told to launch.

That is why the preset was not treated as the source of truth on its own, and why the grep in §4
is over the whole tree rather than over the presets file.

---

## 2. Linux and macOS — the recommendation, applied

**Recommended scheme: lowercase snake_case, `watis_game*`, on all three platforms.** It is
Talon's Windows decision extended, not a new one:

| | was | now |
|---|---|---|
| Windows client | `watis-world.exe` | **`watis_game.exe`** — his |
| Linux server | `watis-world-server.x86_64` | **`watis_game_server.x86_64`** |
| macOS client | `watis-world.zip` / `watis-world.app` / `Contents/MacOS/watis-world` | **`watis_game.zip`** / `watis_game.app` / `Contents/MacOS/watis_game` |

**Binary names, not display names — and no display name moved.** The Windows preset still carries
`application/product_name="Watis World"`; `Branding.Wordmark` is still `"Watis World"`; the
exported client's window title is still `Watis World` (verified on the boot run, §3). The one
user-visible consequence is on macOS, where the bundle's *filename* is what Finder labels: it
will read `watis_game`, not `Watis World`. That is a value call and it is stated rather than
smuggled — the alternative was one platform naming itself unlike the other two, which is exactly
the state this packet was sent to end.

### The uncommitted change in `C:\repos\Watis_Game` — read, not touched, and superseded

`git -C C:/repos/Watis_Game diff -- deploy/Export-MacClient.ps1` (read-only; that tree was not
modified, and nothing was built or run in it) shows an unowned, uncommitted edit that renames the
mac bundle to `Watis World.app` with a binary `Watis World`. It is six assertion strings and
nothing else — lines 174–175, 188, 191 and 203 — with no matching change to `export_presets.cfg`,
so **on its own it is a broken tree**: `export_path` still says `watis-world.zip`, Godot would
still emit `watis-world.app`, and every one of those six assertions would fail. It is a
half-rename of exactly the kind §1 describes, caught in the loud direction.

**It is superseded either way and should be discarded, not merged.** It predates today's decision,
it disagrees with `watis_game` in two directions at once (spaces in a path SteamPipe and WSL both
have to carry, and a display name where the other two platforms use a machine name), and STEAM-1
has rewritten every line it touched. Nothing in it is lost: the display name it was reaching for
already exists as `Branding.Wordmark` and `application/product_name`.

**Genuinely ambiguous, and therefore an open question rather than a decision:** whether the macOS
bundle should carry `Watis World` as its Finder-visible label while keeping `watis_game` as the
executable inside. Godot's macOS export derives both from the export path's filename, so that
split is not free — it needs either a post-export rename plus a re-signature, or a preset option
this repo does not currently set. Nobody has a Mac to check it on. Raised in Open questions; not
invented here.

---

## 3. The App ID `4951240`

**Wired as a parameter, documented in one place, committed into no template.**

`deploy/steam/README.md` now carries a numbers table (App ID, client depot, mac depot, executable,
install folder), the exact invocation, a `-DryRun` rehearsal line, and where each number comes
from. `app_build.vdf.template` and `depot_build_client.vdf.template` are **unchanged** — they keep
`%APP_ID%` / `%DEPOT_ID_CLIENT%`, `Upload-Steam.ps1` substitutes at render time, and the rendered
file lands in gitignored `_generated/`. The comment's reasoning ("no real Steamworks IDs ever need
to be committed here") holds precisely as well with a real number in hand as it did without one.

**Depot IDs come from Steamworks and nowhere else:** App Admin → SteamPipe → Depots. The client
depot on record is **1718371**, paired with `4951240` in
`2026-08-29-STEAM-STATE-private-playtest.md` §4 and dated 2026-07-25. The mac depot has never been
minted and still blocks the macOS upload only.

### The conflict, stated rather than resolved

`4951240` is recorded in this repo as the **Playtest app**, and §4c of that same document ruled on
2026-08-30 that the Playtest app is **not** the vehicle — Talon's words were *"I would like an
unreleased playtest page with private keys"*, and the route chosen was **Release Override keys on
the unreleased base app**, because Valve documents that a Playtest must be *released* to be
playable and that a released app's details surface to players and third-party crawlers *"even if
it is released with a 'Hidden' store page"*. `PLAYTEST-SETUP-CHECKLIST.md` §6 said, in bold, *"do
not use it"*.

Today's message hands over `4951240` and says playtest keys have been requested. **Only Talon can
say whether that is a deliberate reversal of §4c or a number belonging to a different app.** Per
`CLAUDE.md` the document is what is wrong when his direction and a document disagree, so both the
README and the checklist now carry the question beside the command instead of continuing to
assert "do not use it" — but neither invents an answer, and §4c's reasoning is left standing as
history rather than deleted. It is one plain question and it is in Open questions.

### `steam_appid.txt` — the ruling, and the enforcement

**It does not ship in the depot.** Reasons, in order:

1. **It overrides.** `SteamService.ResolveAppId` ranks Valve's `SteamAppId`/`SteamGameId` above
   the file, so this repo's own resolution is safe — but the file is also read by Valve's SDK
   during `SteamAPI_Init`, which is not a path this repo controls. A stale number shipped beside
   the exe therefore outlives every later App ID change, and it fails on a tester's machine
   rather than on this one. A number that lives only on `Upload-Steam.ps1`'s command line cannot
   go stale in a depot.
2. **Valve's framing of the file is development-time**, not distribution.
3. It was already gitignored (`.gitignore:26`) and already absent from the export — but "absent
   because nobody happened to make one" is not a guarantee, and the packet asked for the export
   to *do* what was concluded.

**Demonstrated, not asserted.** `deploy/Export-WindowsClient.ps1` now walks the export directory
and fails the export if the file is there. On a clean export it prints:

```
[export] no steam_appid.txt in the export - dev-only override correctly excluded
```

The directory is wiped and re-exported immediately above the check, so a hit can only mean the
export itself carried the file in — which is the thing the guard exists to catch.

**A local non-Steam run still works, three ways:** drop your own `steam_appid.txt` beside the
exported exe (gitignored, `TryAppIdFile` probes the exe's directory), pass
`--steam-app-id 4951240`, or set `SteamAppId` in the shell. Nothing about the developer path
changed.

### The Spacewar (480) fallback stays — and why that is the safe answer, not the lazy one

Confirmed by reading `SteamService.ResolveAppId`: with no `--steam-app-id`, no `SteamAppId` /
`SteamGameId`, no `STEAM_APP_ID` and no `steam_appid.txt`, resolution returns
`NetProfile.FallbackSteamAppId` = **480**. **Not changed by this packet**, deliberately:

1. **480 fails safe; a real ID fails hard.** Spacewar initialises for every logged-in Steam
   account. A real App ID compiled into the binary makes `SteamAPI.Init()` fail for anyone who
   does not own that app — and this code path, by construction, only runs when Steam did *not*
   launch the build, i.e. on a developer's machine or a tester doing something unusual. The
   fallback's whole job is to degrade gracefully; a real ID there degrades worse.
2. **It never fires on the route being shipped.** A Steam-launched build resolves from Valve's
   env long before reaching it — that is the defect MRF-A fixed on 2026-08-30.
3. **It is not a one-constant change.** §3b pairs it with `NetProfile.GameTag` ("change it with
   the App ID"), the lobby-directory scope key. Changing that mid-playtest means builds already
   in testers' hands can no longer see rooms hosted by new builds. That is a live compatibility
   break during a running playtest, and the packet's scope excludes protocol changes.

**Revisit trigger, recorded in three places** (`deploy/steam/README.md`, §3b of the STEAM-STATE
doc, and `DECISION-LOG.md` §8): change `FallbackSteamAppId` and `GameTag` together, in one commit,
once it is settled which app `4951240` is — then re-run `tests/Run-SteamLogicTest.ps1`, which
asserts the resolution *order*, not the value, and should stay green.

---

## 4. `watis_install_folder` — the finding is that nothing in the repo needs it

It is the Steamworks **Installation → Install Folder** field: the directory created under
`steamapps/common/` on a player's disk. **Nothing in this repository reads it, and nothing should.**
Checked, rather than assumed:

- **The depot scripts describe local paths, not installed ones.** `depot_build_client.vdf`'s
  `ContentRoot` is `..\..\..\build\windows-client\` — a path on the uploading machine — and its
  `FileMapping` maps `LocalPath "*"` to `DepotPath "."`, the depot root. Steam decides where that
  root lands on disk; the depot never names the folder. `app_build.vdf`'s `ContentRoot` is
  likewise the local `build\` directory.
- **The game resolves nothing by install-folder name.** Every filesystem path it needs comes from
  `OS.GetExecutablePath()` (`SteamService.cs:125,442`, `LocalServerHost.cs:69`,
  `NetworkManager.cs:520`) or from `user://`. `grep -rn "steamapps"` over `scripts/`, `deploy/`
  and `docs/store/` returns nothing.

Recorded in `deploy/steam/README.md` with that reasoning, so the next person inherits the negative
instead of re-deriving it. **The install folder does have one hard partner-site consequence** and
it is on Talon's closing checklist: the **Launch Options → Executable** field must read
`watis_game.exe`, because Steam installs the depot into `watis_install_folder` and then runs
whatever that field names.

---

## 5. `docs/store/` — corrected against the tree, not against another document

BRAND-1 landed on the base branch, so every "the build renders `WORKING TITLE`" claim is now
false. Verified once, at source: `scripts/ui/Branding.cs:25` is
`public const string Wordmark = "Watis World"` and `:36` is
`public const string Studio = "Great-Grand-Software"`.

| File | Was | Now |
|---|---|---|
| `PLAYTEST-SETUP-CHECKLIST.md` §5 | "`Branding.cs:19` is `Wordmark = "WORKING TITLE"`"; three names in play | Quotes `Branding.cs:25` / `:36` and the real strings; two names in play; the Store Name field is explicitly left as Talon's |
| `store-description.md` (changelog note) | "`Branding.Wordmark` is `"WORKING TITLE"` … today" | `"Watis World"` at `Branding.cs:25`, with the old wording kept as a dated correction |
| `store-description.md` (2026-08-29 block) | "it is `"WORKING TITLE"` now, verified at `Branding.cs:19`" | `"Watis World"`, verified at `Branding.cs:25` |
| `store-description.md` (rewrite-held reason 1) | "The name is unresolved … three names" | Struck: the name resolved 2026-09-04; only the partner-site Store Name field is open |
| `2026-08-29-STEAM-STATE…` §1 row 2 | `Branding.Wordmark` is `"WORKING TITLE"` | `"Watis World"` + `Studio`, dated |
| `2026-08-29-STEAM-STATE…` §2 heading + body | "The thing actually blocking a store page is the name"; three names | Name landed; two names; capsule art no longer name-blocked |

Corrected in the same pass, because they were wrong for the same reason and a reader would quote
them:

- **`Branding.cs:25` was cited twice as the wordmark *image* path.** The file grew; line 25 is now
  `Wordmark` itself and the image path is `:42`. Fixed in `2026-08-29-STEAM-STATE…` §1 row 1 and
  in `capsule-spec.md`. (`resources/TitleWordmark.png` still does not exist — that half was and
  is true.)
- **`.gitignore:32` cited for `steam_appid.txt`** — it is `:26`. Fixed in §3b row 3.
- **§3 item 6, "Store Name field, once §2 resolves"** — §2 has resolved; now stated as unblocked.
- **The executable name** now appears in `PLAYTEST-SETUP-CHECKLIST.md` §6 as `watis_game.exe`,
  with the partner-site Executable / Install Folder fields as an explicit pre-upload box.
- **§1 and §6's "do not use `4951240`"** now carry the 2026-09-04 update rather than contradicting
  Talon's own message on the day he is about to use the checklist.

**Not decided here, on purpose:** the Steamworks **Store Name** field. The checklist keeps treating
it as distinct from the in-game wordmark, which is correct, and it is Talon's.

---

## 6. Verification

### 1. Build

```
dotnet build WatisWorld.sln
    4 Warning(s)   0 Error(s)      Time Elapsed 00:00:05.38
```

The same four warnings `DECISION-LOG.md` §4 names (`AvatarClipDirector` CS0618, two
`AvatarVisual` CS8604, `SandboxCamera` CS8602). No C# was touched by this packet.

### 2. Export

```
[export] BuildInfo.Version=2026.08.30 -> Windows version quad 2026.8.30.0
[export] version stamp verified - export_presets.cfg matches BuildInfo.Version
[export] building Windows client export...
[export] steam_api64.dll present beside C:\repos\Watis-steam1\build\windows-client\watis_game.exe
[export] no steam_appid.txt in the export - dev-only override correctly excluded
[export] OK - C:\repos\Watis-steam1\build\windows-client\watis_game.exe (190 MB total)
```

exit 0, **0 ERROR lines**.

| | this run | recorded | delta |
|---|---|---|---|
| `watis_game.exe` | **114,617,584 bytes** | `watis-world.exe` 111,831,216 bytes (MOVE-1 section, 2026-09-04) | +2,786,368, **+2.5%** |
| export tree | **199,266,417 bytes** (190 MB) | 187 MB (`DECISION-LOG.md` §4, 2026-09-02) | ≈ +3 MB |

No surprise in either: the base branch has taken LEVEL-1, HONK-1, SHADER-1, SHADER-2, CELEBRATE-1,
BRAND-1, COPY-1 and FIX-1 since those figures were recorded, and a 2.5 % growth over a week of
level and audio content is unremarkable. Neither figure moved because of the rename — a filename
is not payload.

*The first export of the session ran on a cold `.godot` (a fresh worktree) and printed the
documented UITheme-before-fontdata ordering ERRORs; §4 records exactly that artefact for a cold
import. It was re-run after `godot --headless --path . --import` (exit 0, **0 ERROR lines**), and
the numbers above are from that clean run.*

### 3. The exported client boots

Launched `build\windows-client\watis_game.exe` (not headless), held it 40 s, killed it. Process
alive at 30 s and again at 40 s, `MainWindowTitle` = **`Watis World`** at both checks. Its own
output, in full apart from the C# backtrace frames:

```
Godot Engine v4.7.stable.mono.official.5b4e0cb0f - https://godotengine.org
Vulkan 1.4.329 - Forward+ - Using Device #0: NVIDIA - NVIDIA GeForce RTX 4070

[graphics] tier High (ship default)
[display] window mode: wanted=fullscreen actual=Fullscreen (user://settings.cfg [display] fullscreen)
WARNING: Realtime Skies can only use a radiance size of 256. Radiance size will be set to 256 internally.
   at: set_radiance_size (servers/rendering/renderer_rd/environment/sky.cpp:594)
       [3] void MpFoundation.Ui.Menu.MenuBackdrop.BuildWorld(Godot.SubViewport)
       [4] void MpFoundation.Ui.Menu.MenuBackdrop.Build()
       [5] void MpFoundation.Ui.Menu.MenuBackdrop._Ready()
      [12] void MpFoundation.Ui.Menu.MainMenu._Ready()
```

**0 ERROR lines. 1 WARNING** (the Realtime-Skies radiance clamp, engine-internal and benign). The
backtrace is the receipt this criterion asked for: it can only have been printed from inside
`MainMenu._Ready() -> MenuBackdrop.Build() -> BuildWorld()`, so the main menu genuinely
constructs and renders. This matches `DECISION-LOG.md` §4's recorded boot for the old binary
line for line, which is the point — the rename changed the filename and nothing else.

*One note: the build restored fullscreen from the shared `user://settings.cfg`, so it took the
screen for those 40 s while Talon's playtest was running in the other tree. No lasting effect —
it was killed, and it wrote nothing a menu visit does not.*

### 4. ABSENCE check, with a positive control

`grep -rn "watis-world" .` (`.git/` excluded) over the whole tree returns **five** hits, all
deliberately kept:

| Hit | Why it stays |
|---|---|
| `DECISION-LOG.md:39` | the 2026-09-02 extraction record — `mp-foundation.*` → `watis-world.*`, a dated account of a rename that happened |
| `DECISION-LOG.md:227` | §3's recorded Linux export command, as it was run on 2026-09-02 |
| `DECISION-LOG.md:260` | §4's recorded boot evidence for the binary as it was then named |
| `DECISION-LOG.md:416` | the MOVE-1 byte count this report's §6.2 compares against |
| `docs/agents/roles/…/2026-09-04-MOVE-1-report.md:150` | a dated outbox report's measurement table |

All five are historical measurements or dated records. Rewriting them would falsify the record —
`DECISION-LOG.md` §8 states the rename, which is the correct place for it. Nothing under
`deploy/`, `scripts/`, `tests/`, `docs/store/`, `export_presets.cfg` or `project.godot` names the
old binary.

**Positive control.** The grep is only worth anything if it can still catch a stray, so one was
planted:

```
$ echo 'launch: build/windows-client/watis-world.exe' > scratch-positive-control.txt
$ grep -rn "watis-world" . | grep -v '^./.git/'
    ... the five kept hits ...
    ./scratch-positive-control.txt:1:launch: build/windows-client/watis-world.exe   <-- caught
$ rm scratch-positive-control.txt
$ grep -rn "watis-world" . | grep -v '^./.git/'
    ... the same five hits, and nothing else ...
```

The planted line appeared, was the only difference, and disappeared with the file. The scratch
file is deleted and was never staged.

### 5. `steam_appid.txt`

Ruling, reasoning and the export's enforcement are in §3 above. The export prints the check's
result on every run; the criterion asked for it to be demonstrated and the export output in §6.2
is that demonstration.

### 6. `docs/store/`

Table in §5. Every claim was re-checked against `scripts/ui/Branding.cs` and `.gitignore` in this
worktree, not against another document.

### 7. The suites

Both commands, on the tip, in the foreground, redirected to a file and read from the file.

**xUnit — `dotnet test tests/unit/SailNet.Tests.csproj`**

```
tip       Passed! - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288, Duration: 3 s
baseline  Passed! - Failed: 0, Passed: 2288, Skipped: 0, Total: 2288, Duration: 3 s
```

**My own baseline, measured — not inherited.** The second line is from a throwaway detached
worktree (`git worktree add --detach C:\repos\Watis-steam1-baseline playtest/2026-09-04-combined`
@ `1f4d688`), so the tip's counts are compared against a number I measured today rather than
against `DECISION-LOG.md` §4's 1681 (which predates a week of merges). **Identical, as it must be:
this packet touches no C#.**

**Scene marathon — `powershell -File tests/Run-AllTests.ps1 -MutexTimeoutMinutes 180`**

Raw, counted from the run's own `=== summary ===` block with the anchored pattern
`^  .+ (PASS|FAIL)$`:

```
48 suite lines   47 PASS / 1 FAIL      OVERALL: FAIL
```

48, not `DECISION-LOG.md` §4's 40 — the registry has grown by eight since 2026-09-02 (the bike,
movement-lab, level-kit, egg, watcher, honk and last-bubble suites the week's packets added).
Counted from this run's own lines, per the rule.

**The single red: `World: tidal-cycle phase`. Discriminated as a load flake, not a regression.**

It is named in `.claude/rules/test-suite.md` as known load-sensitive — *"a two-client phase
comparison with a 1.5 s tolerance — compare the divergence"* — so the verdict was not accepted
either way without measuring. **16 runs**, tip and baseline:

| Tree | Result |
|---|---|
| Tip — marathon + 9 standalone (`-SkipBuild`) | **8 PASS / 2 FAIL** |
| Baseline — detached worktree at `1f4d688`, 6 runs | **6 PASS / 0 FAIL** |
| Last 3 pairs, **interleaved** baseline/tip on a quiet machine | 3/3 PASS each |

**Both tip failures happened on a loaded machine** — the first immediately after the 48-suite
marathon, the second with two Godot processes still alive (Talon's playtest ran throughout; no
process of his was investigated or killed). Neither occurred once the machine was quiet.

**The raw quantity, which is the actual discriminator, is bit-identical across every failure:**

```
cross-view: checked 147 matched sample pairs, all within 1.5s
  - CycleEarly1 0.43159723 vs CycleEarly2 0.5586111 ... differ by 1.52416644s (tolerance 1.5s)
  - ... 0.43215278 vs 0.6015046 ................... differ by 2.03222184s (tolerance 1.5s)
  - late-join: CycleLate 0.6071991 vs CycleEarly1 0.43243057 ... by 2.09722236s
```

147 of 152 matched pairs inside tolerance in **every** run, passing and failing alike; the five
that miss do so at one instant, by the same three numbers (1.52416 s, 2.03222 s, 2.09722 s) in the
marathon and in standalone run 1 independently. A real phase divergence would wander. This is
quantised — 1.524 s is 0.127 of the 12 s period, i.e. one client's phase sample sitting a snapshot
tick or two behind at a single moment under scheduler pressure. `--cycle-selftest`, the phase-math
and light-curve half, printed **PASS in all 16 runs, including both failures**.

**And there is no mechanism.** This branch's entire diff is `export_presets.cfg`, three
PowerShell scripts, one SteamPipe template comment and six Markdown files. No C#, no scene, no
`project.godot`. Nothing in it can reach `CycleDriver`.

**What I am not claiming:** I did not reproduce the failure on the baseline, because I never ran
the baseline under comparable load. The honest statement is the one above — the tip passes
whenever the machine is quiet, and its two failures both landed while it was not.

Measured entry added to `.claude/rules/test-suite.md` with the evidence and the date, per the
role's measured-entries-only grant. It also records a trap found on the way: **`-SkipBuild` on a
fresh worktree fails with `Cannot find path ...\tests\logs\cycle-selftest.out.log`**, because
`-SkipBuild` skips the `Reset-LogDir` that creates `tests/logs`. It looks exactly like a suite red
and is not one; the baseline worktree hit it three times before the first full run created the
directory.

---

## Bible check

```
Bibles applied:  None.
Items checked:   —
Result:          n/a — no bible applies.
```

This packet changed export filenames, three PowerShell build scripts, a SteamPipe comment and
five documents. It touched no C#, no scene, no gameplay system, nothing autonomous, nothing the
player acts on, no composition and nothing intended to make anyone feel anything. The exported
binary's behaviour is byte-identical apart from its name, which the boot run in §6.3 confirms.

---

## Open questions — ripe, and both for Talon

**1. Is `4951240` the Playtest app or the base app?**

This is one plain question and it gates the first upload. `2026-08-29-STEAM-STATE-private-playtest.md`
§4 records `4951240` / depot `1718371` as the **Playtest** app pair, and §4c — your own ruling of
2026-08-30, *"I would like an unreleased playtest page with private keys"* — retired the Playtest
app as the vehicle in favour of Release Override keys on the unreleased **base app**. The reason
mattered: Valve says a Playtest has to be *released* to be playable, and a released app's details
reach players and third-party crawlers *"even if it is released with a 'Hidden' store page"* —
which is irreversible, and is the one thing §4c was chosen to avoid.

If `4951240` is the Playtest app and "playtest keys" means Playtest signup keys, that reverses
§4c and the game's existence becomes discoverable when the app is released. That may be exactly
what you want now, and it is your call — but it should be a decision, not a side effect of a
number being handed over. If instead `4951240` is the base app (or you have since made a base app
whose number this is), nothing about §4c changes and the depot ID needs to come from the same app.

Whichever it is, the `-AppId` and `-DepotIdClient` must belong to the **same** app.

**2. Should the macOS bundle carry `Watis World` as its Finder label?**

Applied for now: `watis_game.app`, consistent with Windows and Linux. The open half is whether
the Finder-visible label should be `Watis World` while the executable inside stays `watis_game` —
which is what a shipping Mac app usually does. Godot derives both from the export path's filename,
so the split costs either a post-export rename plus a re-signature (the ad-hoc signature is
verified by `Export-MacClient.ps1`, so it cannot just be renamed) or a preset option this repo
does not set. It is unverifiable here — no Mac has ever launched this build — and it blocks
nothing: the Mac depot ID is still unminted, so no Mac upload is possible yet either way. Worth
one sentence from you before the Mac path is next picked up.

---

## What is still yours on the partner site, before a build can go live

Short and plain. Nothing in this list can be done from the repo.

1. **Confirm which app `4951240` is** (Open question 1) — Playtest app, or base app. Everything
   below depends on it.
2. **Get the client Depot ID for that same app.** App Admin → SteamPipe → Depots. On record for
   `4951240` is `1718371`, dated 2026-07-25 — re-read it off the page rather than trusting a
   fourteen-month-old note.
3. **Check the Executable field says `watis_game.exe`** — Installation → Launch Options. The repo
   now produces exactly that name. A mismatch installs fine and then will not start.
4. **Check the Install Folder field says `watis_install_folder`.** Nothing in the repo depends on
   it; Steam does.
5. **Say go, once**, and the upload runs:
   `powershell -File deploy\steam\Upload-Steam.ps1 -Username <account> -AppId 4951240 -DepotIdClient <depot>`.
   A `-DryRun` rehearsal first costs nothing and renders the SteamPipe scripts without touching
   Steam. First run on this machine prompts for a **Steam Guard code**, so be at the keyboard.
6. **Set the build live on a branch.** Steamworks → SteamPipe → Builds → set live on `default`.
   Uploading alone does not make it playable; this step does.
7. **Hand out the keys** once the build is live, and **verify privacy the hard way** — have
   someone with no key try to find the game. A setting you have never seen fail closed has not
   been shown to work.

Not needed for this playtest, on the ruled route: capsule art, screenshots, a store page, the
Content Survey. Still owed before anything *public*: the Store Name field, a real clearance pass
on the name, the feedback-channel link in `store-description.md:70`, and the Mac Depot ID if Mac
testers are in scope.
