# Steam build/upload pipeline

Turns the exported Windows client into a SteamPipe build on Steamworks.

## Files

- `app_build.vdf.template` / `depot_build_client.vdf.template` — SteamPipe scripts with
  `%APP_ID%` / `%DEPOT_ID_CLIENT%` / `%BUILD_DESC%` placeholders. Never edited in place.
- `Upload-Steam.ps1` — renders the templates into `_generated/` (gitignored, real IDs only
  live there, locally) and runs `steamcmd`.

## One-time setup (per machine that uploads)

1. Install the Steamworks SDK Tools (`steamcmd`) — download from partner.steamgames.com,
   or use any `steamcmd` distribution, and make sure `steamcmd.exe` is on PATH (or pass
   `-SteamCmd <path>`).
2. Have a Steamworks account with publish permissions on the app.

## The numbers, as of 2026-09-04 (STEAM-1)

Talon, from the Steamworks partner site, 2026-09-04:

> "The install folder is now called `watis_install_folder` and the executable is called
> `watis_game.exe` This is the number, by the way 4951240. I've requested playtest keys."

| Field | Value | Where it lives |
|---|---|---|
| **App ID** | **4951240** | `Upload-Steam.ps1 -AppId` only. **Not committed into the templates** — `app_build.vdf.template` says why, and that reasoning still holds: the templates carry `%APP_ID%`, `Upload-Steam.ps1` substitutes, and the rendered file lands in gitignored `_generated/`. |
| **Client Depot ID** | **1718371** (see the caveat below) | `Upload-Steam.ps1 -DepotIdClient`. Minted only by Steamworks: App Admin → **SteamPipe** → **Depots**. Nothing can invent or guess one. |
| **Mac Depot ID** | not minted | Same page, **+ Add Depot**. Blocks the macOS upload only; the macOS build itself exports and verifies without it. |
| **Windows executable** | **`game.exe`** (Talon, 2026-09-05 — was `watis_game.exe`) | `export_presets.cfg` `[preset.1] export_path`, and `Export-WindowsClient.ps1`. The partner site's **Installation → Launch Options → Executable** field must say the same string, or Steam installs the depot and then cannot start it. |
| **Install folder** | **`Watis World`** (Talon, 2026-09-05) | **Partner site only — nothing in this repo needs it.** See below. |

**Which app is 4951240?** `docs/store/2026-08-29-STEAM-STATE-private-playtest.md` §4 recorded
`4951240` / depot `1718371` as the **Playtest** app pair (provided 2026-07-25), and §4c then
ruled the Playtest app was *not* the vehicle — the ruling was Release Override keys on the
**unreleased base app**. Talon's 2026-09-04 message supplies `4951240` as "the number" and says
he has requested playtest keys, which reads as the Playtest route being back on. **That is his
call and it is not settled in this repo** — the one consequence worth stating before the first
upload is Valve's own: a Playtest app has to be *released* to be playable, and a released app's
details surface to players and third-party crawlers *"even if it is released with a 'Hidden'
store page"*. Confirm which app the `-AppId` belongs to before uploading; the depot ID must come
from the same app.

### Why the install folder is not a repo value

`watis_install_folder` is the Steamworks **Installation → Install Folder** field: the directory
name created under `steamapps/common/` on a player's disk. Nothing in this repository reads it,
and nothing should:

- The depot scripts describe **local** paths, not installed ones — `depot_build_client.vdf`'s
  `ContentRoot` is `build\windows-client\` on the uploading machine, and its `FileMapping`
  maps `LocalPath "*"` to `DepotPath "."`, i.e. the depot root. Steam decides where that root
  lands; the depot never names the folder.
- The game never resolves anything by install-folder name. Every path it needs it derives from
  `OS.GetExecutablePath()` (`SteamService`, `LocalServerHost`, `NetworkManager`) or from
  `user://`, neither of which contains the install folder's name.

So it is recorded here and nowhere else. If it ever *does* need to appear in code, that is a new
decision, not a missing one.

### `steam_appid.txt` — dev-only, and kept out of the depot

**Ruling (STEAM-1, 2026-09-04): it does not ship.** `Export-WindowsClient.ps1` asserts its
absence from the export directory and fails the export if it appears — the check prints
`[export] no steam_appid.txt in the export - dev-only override correctly excluded`.

Reasons, in order: the file **overrides** whatever App ID Steam itself handed the process, so a
stale number shipped beside the exe survives every later App ID change and fails on a tester's
machine rather than here; and Valve's own framing of the file is development-time, not
distribution. It is gitignored (`.gitignore:26`) so it is not in the tree to begin with.

A local **non-Steam** run is unaffected, three ways: drop your own `steam_appid.txt` beside the
exported exe (`SteamService.TryAppIdFile` probes the exe's directory), or pass
`--steam-app-id 4951240`, or set `SteamAppId` in the shell. On the ruled route a tester always
launches from their Steam library, so Valve's `SteamAppId`/`SteamGameId` hand-off is what
actually resolves — `SteamService.ResolveAppId` reads those *above* both the file and the
project's own `STEAM_APP_ID`.

### The Spacewar (480) fallback stays, for now

With no env and no `steam_appid.txt`, `ResolveAppId` falls through to
`NetProfile.FallbackSteamAppId` = **480** (Valve's Spacewar). **Unchanged by STEAM-1**, and
deliberately:

- It fails **safe**. Spacewar initialises for every logged-in Steam account. A real App ID
  compiled into the binary fails `SteamAPI.Init()` for anyone who does not own that app — a
  worse failure, in a code path that by construction only runs when Steam did *not* launch the
  build.
- It never fires on the route being shipped. A Steam-launched build resolves from Valve's env
  long before reaching the fallback.
- Swapping it is not a one-constant change. `2026-08-29-STEAM-STATE-private-playtest.md` §3b
  pairs it with `NetProfile.GameTag` (the lobby-directory scope key), and changing that mid-
  playtest makes new builds unable to see rooms hosted by builds already in testers' hands.

**Revisit trigger:** change both, in one commit, when the App ID the shipped build runs under is
confirmed (see "Which app is 4951240?" above), and re-run `tests/Run-SteamLogicTest.ps1` — it
asserts the resolution *order*, not the value, so it should stay green.

## Every release

```powershell
powershell -File deploy\Export-WindowsClient.ps1
powershell -File deploy\steam\Upload-Steam.ps1 -Username <steamworks-account> -AppId 4951240 -DepotIdClient 1718371
```

Rehearse it first — `-DryRun` renders the SteamPipe scripts, prints them, and never calls
`steamcmd`, never tags, never touches `BUILD-LOG.md`:

```powershell
powershell -File deploy\steam\Upload-Steam.ps1 -Username <steamworks-account> -AppId 4951240 -DepotIdClient 1718371 -DryRun
```

First run per machine prompts interactively for a Steam Guard code; `steamcmd` caches the
session afterward. A successful run lands a new build under Steamworks > SteamPipe > Builds —
it still needs to be set live on a branch (e.g. `default`) from the Steamworks web UI.

`Upload-Steam.ps1` refuses to tag a dirty tree, so commit (or stash) and re-export before
uploading; the `v<BuildInfo.Version>` tag it writes has to describe the binary that went up.

## Scope note

Only the interactive client goes through Steam distribution — and the client is the whole
deployment now: the dedicated match server is spawned by the hosting player's own client as
a child process (same exe, `--server` mode), and room codes resolve through Steam's own
Lobby API. There is no separately-deployed server product and no matchmaking service to
host (the old phonebook + its Docker image were retired in the steam-native-matchmaking
pass).

## Which app `4951240` is — SETTLED 2026-09-05

Talon, asked directly: *"Not the base app. I only want playtest app."* The vehicle is the
**Playtest app**, with the client depot he supplied on 2026-07-25. Dry run first:

```
powershell -File deploy\steam\Upload-Steam.ps1 -Username <steam-partner-account> -AppId 4951240 -DepotIdClient 1718371 -DryRun
```

Drop `-DryRun` to upload for real. It will prompt for the account password and Steam Guard.

He made this call with Valve's own confidentiality findings in front of him (see
`docs/store/PLAYTEST-SETUP-CHECKLIST.md`): a Playtest app must be *released* to be playable, and
releasing surfaces the game to players and third-party crawlers permanently, hidden store page or
not. This is an informed reversal of the 2026-08-30 ruling — do not "correct" it back to the base
app.

## Log in to steamcmd once first — otherwise the upload looks like a hang

`Upload-Steam.ps1` pipes steamcmd's output through `Tee-Object` so it can read the
`successfully finished appID ... build (BuildID ...)` line, which is the only reliable success
signal steamcmd gives. That pipeline also swallows steamcmd's **interactive password prompt**, so
on a machine with no cached credentials the script stops dead at `-- type 'quit' to exit --` and
never asks for anything. It is waiting for a password you cannot see it requesting.

Measured 2026-09-05, on this project's first real upload. Fix it once per machine — log in
directly, OUTSIDE this script:

```
C:\Users\<you>\steamcmd\steamcmd.exe +login <username>
```

Enter the password (it does not echo) and the Steam Guard code, wait for `Waiting for user
info...OK`, then `quit`. Every later run reads the cache and needs no prompt.

**Do not "fix" this by removing the `Tee-Object`** — the captured output is what the
phantom-success guard reads, and that guard is load-bearing: steamcmd returns 0 on some logical
failures, so the BuildID line is the real verdict.

steamcmd is not bundled. Download it from Valve, unzip anywhere outside the repo, and either put
it on PATH or pass `-SteamCmd <full path>`. Its first launch self-updates and exits 7; that is
normal — run it once more and it exits 0.

## The app and depot IDs — CORRECTED 2026-09-05 from the partner site

**The repo had these backwards until 2026-09-05.** Earlier notes recorded "Playtest app 4951240,
client depot 1718371". The partner site says otherwise, and a real upload proved it by failing:

| | AppID | What it is |
|---|---|---|
| **`trill`** | **4951240** | The BASE app (Type: Game), unreleased. A nondescript codename. |
| **`Watis World`** | **5016210** | The **Beta / Playtest app** for trill. This is the upload target. |

Depots under **5016210**:

| Depot | Name | Use |
|---|---|---|
| **5016211** | `trill Playtest Content` | **The Windows client depot.** Referenced by 3 packages, Configuration: All. |
| 5016212 | `trill Playtest Depot mac os` | macOS. **Not referenced by any package** — a build uploaded here reaches nobody until that is fixed. |

`1718371` belongs to a different app. Using it with 5016210 produced
`ERROR! Failed to initialize build on server (Access Denied)` — which is what Steam returns when
the depot is not owned by the app, and it looks exactly like a permissions problem.

### The command

```
powershell -File deploy\steam\Upload-Steam.ps1 -Username <account> -AppId 5016210 -DepotIdClient 5016211 -SteamCmd <path-to-steamcmd.exe>
```

Windows only. Do not pass `-DepotIdMac 5016212` — the script refuses a live macOS upload from
Windows on purpose (the POSIX executable bit does not survive NTFS extraction), and that depot has
no package attached in any case.

### What this means for the confidentiality ruling

Talon's 2026-09-05 ruling — *"Not the base app. I only want playtest app."* — stands, and now points
at the right app: **5016210**. The surfacing consequence recorded in
`docs/store/PLAYTEST-SETUP-CHECKLIST.md` attaches to **5016210**, not to 4951240 as that file's
correction originally said. Releasing the Playtest app to make keys live is what surfaces the game;
the base app `trill` stays unreleased.
